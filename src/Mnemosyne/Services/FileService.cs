using System.IO;
using System.Runtime.CompilerServices;
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

    /// <summary>大文件模式：读头部样本探测编码（forcedEncoding 非空时直接使用，与 Decode 行为一致）</summary>
    public async Task<Encoding> DetectEncodingAsync(string path, Encoding? forcedEncoding = null, CancellationToken cancellationToken = default)
    {
        if (forcedEncoding is not null) return forcedEncoding;
        const int SampleSize = 65536;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        byte[] sample = new byte[SampleSize];
        int read = 0;
        while (read < SampleSize)
        {
            int n = await stream.ReadAsync(sample.AsMemory(read, SampleSize - read), cancellationToken);
            if (n == 0) break;
            read += n;
        }
        return DetectEncodingFromSample(sample, read);
    }

    /// <summary>
    /// 大文件模式：约 1MB/块异步读取并按给定编码增量解码。
    /// Decoder 跨块保留状态，不完整的多字节序列（UTF-8/GBK/UTF-16 同样适用）顺延到下一块解码，
    /// 因此不会在多字节字符中间切断。解码在该枚举的 await 续体上执行，调用方需自行控制线程。
    /// </summary>
    public async IAsyncEnumerable<ReadChunk> ReadChunksAsync(
        string path, Encoding encoding, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        const int ChunkSize = 1024 * 1024;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, useAsync: true);

        // BOM 不特殊跳过：解码为 U+FEFF 留在文首，与小文件路径 Decode 的行为保持一致

        Decoder decoder = encoding.GetDecoder();
        byte[] buffer = new byte[ChunkSize];
        char[] chars = new char[encoding.GetMaxCharCount(ChunkSize)];
        long totalRead = stream.Position;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            totalRead += read;
            int charCount = decoder.GetChars(buffer, 0, read, chars, 0, flush: false);
            if (charCount > 0)
            {
                yield return new ReadChunk(new string(chars, 0, charCount), totalRead);
            }
        }
        int tail = decoder.GetChars(buffer, 0, 0, chars, 0, flush: true);
        if (tail > 0)
        {
            yield return new ReadChunk(new string(chars, 0, tail), totalRead);
        }
    }

    /// <summary>用头部样本探测编码：BOM → 严格 UTF-8（样本截断处先对齐到完整字符边界）→ UDE → GB18030 兜底</summary>
    private Encoding DetectEncodingFromSample(byte[] sample, int length)
    {
        if (TryDetectBom(sample, out Encoding? bomEncoding)) return bomEncoding;
        int safeLength = TrimToUtf8Boundary(sample, length);
        if (TryDecodeStrictUtf8(sample, safeLength, out _)) return EncodingCatalog.Utf8NoBom;
        return DetectWithUde(sample.AsSpan(0, length).ToArray()) ?? Encoding.GetEncoding(54936);
    }

    /// <summary>样本末尾可能切在多字节字符中间；回退不完整 UTF-8 序列，避免严格解码把整块样本判负</summary>
    private static int TrimToUtf8Boundary(byte[] bytes, int length)
    {
        for (int i = length - 1; i >= Math.Max(0, length - 4); i--)
        {
            byte b = bytes[i];
            if ((b & 0x80) == 0) break;
            if ((b & 0xC0) == 0xC0)
            {
                int expected = (b & 0xE0) == 0xC0 ? 2 : (b & 0xF0) == 0xE0 ? 3 : 4;
                if (length - i < expected) return i;
                break;
            }
        }
        return length;
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
        return TryDecodeStrictUtf8(bytes, bytes.Length, out text);
    }

    private static bool TryDecodeStrictUtf8(byte[] bytes, int length, out string text)
    {
        try
        {
            text = StrictUtf8.GetString(bytes, 0, length);
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
