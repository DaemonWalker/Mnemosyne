using System.IO;
using System.Text;
using Mnemosyne.Models;
using UtfUnknown;

namespace Mnemosyne.Services;

/// <summary>
/// 文件 IO 与编码探测：UTF-8 优先 → BOM 识别 → UTF.Unknown 探测（GBK 等）→ GB18030 兜底。
/// 所有方法都可能抛出 IOException/UnauthorizedAccessException，由调用方转化为本地化提示。
/// </summary>
public class FileService
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public async Task<FileReadResult> ReadAsync(string path, Encoding? forcedEncoding = null, CancellationToken cancellationToken = default)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return Decode(bytes, forcedEncoding);
    }

    public async Task WriteAsync(string path, string text, Encoding encoding, CancellationToken cancellationToken = default)
    {
        byte[] preamble = encoding.GetPreamble();
        byte[] body = encoding.GetBytes(text);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 81920, useAsync: true);
        if (preamble.Length > 0) await stream.WriteAsync(preamble, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
    }

    public FileReadResult Decode(byte[] bytes, Encoding? forcedEncoding = null)
    {
        Encoding encoding;
        if (forcedEncoding is not null)
        {
            encoding = forcedEncoding;
        }
        else if (TryDetectBom(bytes, out Encoding? bomEncoding))
        {
            encoding = bomEncoding;
        }
        else if (TryDecodeStrictUtf8(bytes, out string? utf8Text))
        {
            return new FileReadResult(utf8Text, EncodingCatalog.Utf8NoBom, DetectLineEnding(utf8Text));
        }
        else
        {
            encoding = DetectWithUde(bytes) ?? Encoding.GetEncoding(54936); // GB18030 兜底，兼容 GBK 全集
        }

        string text = encoding.GetString(bytes);
        return new FileReadResult(text, encoding, DetectLineEnding(text));
    }

    public static LineEnding DetectLineEnding(string text)
    {
        int crlf = 0;
        int lf = 0;
        int cr = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (text[i] == '\n')
            {
                lf++;
            }
        }
        if (crlf == 0 && lf == 0 && cr == 0) return LineEnding.CrLf;
        if (crlf >= lf && crlf >= cr) return LineEnding.CrLf;
        return lf >= cr ? LineEnding.Lf : LineEnding.Cr;
    }

    private static bool TryDetectBom(byte[] bytes, out Encoding encoding)
    {
        encoding = null!;
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            encoding = new UTF32Encoding(bigEndian: false, byteOrderMark: true);
            return true;
        }
        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        {
            encoding = new UTF32Encoding(bigEndian: true, byteOrderMark: true);
            return true;
        }
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = EncodingCatalog.Utf8Bom;
            return true;
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = Encoding.Unicode;
            return true;
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = Encoding.BigEndianUnicode;
            return true;
        }
        return false;
    }

    private static bool TryDecodeStrictUtf8(byte[] bytes, out string text)
    {
        try
        {
            text = StrictUtf8.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    private static Encoding? DetectWithUde(byte[] bytes)
    {
        try
        {
            DetectionResult result = CharsetDetector.DetectFromBytes(bytes);
            DetectionDetail? detected = result.Detected;
            if (detected?.Encoding is { } encoding && detected.Confidence > 0.5f)
            {
                // UDE 可能报出 UTF-8/ASCII，但严格 UTF-8 已经失败，说明探测结果不可信时直接丢弃
                if (encoding.CodePage == 65001 || encoding.CodePage == 20127) return null;
                return encoding;
            }
        }
        catch (Exception)
        {
            // 探测失败走兜底编码
        }
        return null;
    }
}
