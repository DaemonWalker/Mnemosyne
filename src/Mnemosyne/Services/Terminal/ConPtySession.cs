using System.ComponentModel;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace Mnemosyne.Services.Terminal;

/// <summary>
/// 一个 ConPTY shell 会话：创建伪控制台、以伪控制台为附加目标启动 shell 进程，
/// 后台循环读取输出并经 OutputReceived 上抛；进程退出时触发 Exited。
/// </summary>
public sealed class ConPtySession : IDisposable
{
    // ConPTY 需要 Windows 10 1809（build 17763）
    public static bool IsSupported => Environment.OSVersion.Version >= new Version(10, 0, 17763);

    private IntPtr _pseudoConsole;
    private FileStream? _outputStream;
    private FileStream? _inputStream;
    private SafeProcessHandle? _processHandle;
    private int _exitCode = -1;
    private bool _disposed;

    /// <summary>shell 输出到达（后台线程，字节流为控制台输出编码，ConPTY 默认 UTF-8）</summary>
    public event Action<byte[]>? OutputReceived;

    /// <summary>shell 进程退出（后台线程），参数为退出码</summary>
    public event Action<int>? Exited;

    public bool IsRunning => _processHandle is not null && _exitCode < 0;

    public void Start(string shellExe, string workingDirectory, int cols, int rows)
    {
        if (!IsSupported) throw new PlatformNotSupportedException();

        var sa = new ConPtyNative.SecurityAttributes
        {
            nLength = System.Runtime.InteropServices.Marshal.SizeOf<ConPtyNative.SecurityAttributes>(),
            bInheritHandle = true,
        };

        // ConPTY 输入管道：我们写、伪控制台读；输出管道：伪控制台写、我们读
        if (!ConPtyNative.CreatePipe(out SafeFileHandle? inputRead, out SafeFileHandle? inputWrite, ref sa, 0))
            throw new Win32Exception();
        if (!ConPtyNative.CreatePipe(out SafeFileHandle? outputRead, out SafeFileHandle? outputWrite, ref sa, 0))
        {
            inputRead.Dispose();
            inputWrite.Dispose();
            throw new Win32Exception();
        }

        var size = new ConPtyNative.Coord { X = (short)cols, Y = (short)rows };
        int hr = ConPtyNative.CreatePseudoConsole(size, inputRead.DangerousGetHandle(), outputWrite.DangerousGetHandle(), 0, out _pseudoConsole);
        if (hr != 0)
        {
            inputRead.Dispose();
            inputWrite.Dispose();
            outputRead.Dispose();
            outputWrite.Dispose();
            throw new Win32Exception(hr);
        }

        // 伪控制台已持有自己一份句柄，这两端可以关闭
        inputRead.Dispose();
        outputWrite.Dispose();

        IntPtr attributeList = IntPtr.Zero;
        try
        {
            IntPtr attributeSize = IntPtr.Zero;
            ConPtyNative.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeSize);
            attributeList = System.Runtime.InteropServices.Marshal.AllocHGlobal(attributeSize);
            if (!ConPtyNative.InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeSize))
                throw new Win32Exception();
            if (!ConPtyNative.UpdateProcAttribute(
                    attributeList, 0, (IntPtr)ConPtyNative.ProcThreadAttributePseudoConsole,
                    _pseudoConsole, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception();

            var startupInfo = new ConPtyNative.StartupInfoEx();
            startupInfo.StartupInfo.cb = System.Runtime.InteropServices.Marshal.SizeOf<ConPtyNative.StartupInfoEx>();
            startupInfo.lpAttributeList = attributeList;

            if (!ConPtyNative.CreateProcessW(
                    null, shellExe, IntPtr.Zero, IntPtr.Zero, false,
                    ConPtyNative.ExtendedStartupInfoPresent | ConPtyNative.CreateUnicodeEnvironment,
                    IntPtr.Zero, workingDirectory, ref startupInfo, out ConPtyNative.ProcessInformation pi))
                throw new Win32Exception();

            ConPtyNative.CloseHandle(pi.hThread);
            _processHandle = new SafeProcessHandle(pi.hProcess, true);
        }
        catch
        {
            ConPtyNative.ClosePseudoConsole(_pseudoConsole);
            _pseudoConsole = IntPtr.Zero;
            inputWrite.Dispose();
            outputRead.Dispose();
            throw;
        }
        finally
        {
            if (attributeList != IntPtr.Zero)
            {
                ConPtyNative.DeleteProcThreadAttributeList(attributeList);
                System.Runtime.InteropServices.Marshal.FreeHGlobal(attributeList);
            }
        }

        _inputStream = new FileStream(inputWrite, FileAccess.Write, 4096);
        _outputStream = new FileStream(outputRead, FileAccess.Read, 4096);
        Task.Run(ReadOutputLoopAsync);
        Task.Run(WaitForExit);
    }

    private async Task ReadOutputLoopAsync()
    {
        byte[] buffer = new byte[8192];
        try
        {
            while (!_disposed && _outputStream is not null)
            {
                int read = await _outputStream.ReadAsync(buffer).ConfigureAwait(false);
                if (read == 0) break;
                if (read == buffer.Length)
                {
                    OutputReceived?.Invoke(buffer);
                }
                else
                {
                    byte[] chunk = new byte[read];
                    Array.Copy(buffer, chunk, read);
                    OutputReceived?.Invoke(chunk);
                }
            }
        }
        catch (Exception) when (_disposed)
        {
        }
        catch (Exception)
        {
            // 管道被关闭（伪控制台销毁）等情况，视为会话结束，由退出监控兜底
        }
    }

    private void WaitForExit()
    {
        if (_processHandle is null) return;
        ConPtyNative.WaitForSingleObject(_processHandle, ConPtyNative.Infinite);
        if (!ConPtyNative.GetExitCodeProcess(_processHandle, out uint code)) code = 0xFFFFFFFF;
        _exitCode = (int)code;
        if (!_disposed) Exited?.Invoke(_exitCode);
    }

    public void Write(byte[] data)
    {
        if (_disposed || !IsRunning) return;
        try
        {
            _inputStream?.Write(data, 0, data.Length);
            _inputStream?.Flush();
        }
        catch (Exception) when (_disposed || !IsRunning)
        {
        }
        catch (IOException)
        {
        }
    }

    public void Resize(int cols, int rows)
    {
        if (_disposed || _pseudoConsole == IntPtr.Zero) return;
        ConPtyNative.ResizePseudoConsole(_pseudoConsole, new ConPtyNative.Coord { X = (short)cols, Y = (short)rows });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_pseudoConsole != IntPtr.Zero)
        {
            // 先关伪控制台让 shell 自然退出，再兜底强杀（shell 可能已退）
            ConPtyNative.ClosePseudoConsole(_pseudoConsole);
            _pseudoConsole = IntPtr.Zero;
        }
        if (_processHandle is not null)
        {
            if (IsRunning) ConPtyNative.TerminateProcess(_processHandle, 0);
            _processHandle.Dispose();
            _processHandle = null;
        }
        _outputStream?.Dispose();
        _inputStream?.Dispose();
    }
}
