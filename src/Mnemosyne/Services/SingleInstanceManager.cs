using System.IO;
using System.IO.Pipes;
using System.Text;

namespace Mnemosyne.Services;

public sealed class SingleInstanceManager : IDisposable
{
    private const string MutexName = @"Local\Mnemosyne.SingleInstance";
    private const string PipeName = "Mnemosyne.SingleInstance.Pipe";

    private readonly Mutex _mutex;
    private readonly bool _isPrimary;
    private CancellationTokenSource? _cts;

    public SingleInstanceManager()
    {
        // initiallyOwned 设 false，避免 ReleaseMutex 的线程亲和问题；存在性本身即锁
        _mutex = new Mutex(false, MutexName, out bool createdNew);
        _isPrimary = createdNew;
    }

    public event Action<string[]>? ArgsReceived;

    public bool TryBecomePrimary(string[] args)
    {
        if (_isPrimary) return true;

        // 次实例：把命令行参数经命名管道转发给首实例后直接退出
        try
        {
            using NamedPipeClientStream client = new(".", PipeName, PipeDirection.Out);
            client.Connect(1000);
            byte[] payload = Encoding.UTF8.GetBytes(string.Join('\n', args));
            client.Write(payload, 0, payload.Length);
        }
        catch (Exception)
        {
            // 首实例管道暂不可用（如正在退出），次实例静默退出即可
        }
        return false;
    }

    public void StartListening()
    {
        if (!_isPrimary) return;
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using NamedPipeServerStream server = new(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(token);
                    using StreamReader reader = new(server, Encoding.UTF8);
                    string payload = await reader.ReadToEndAsync(token);
                    string[] args = payload.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    // 即使无参数也要激活首实例窗口
                    ArgsReceived?.Invoke(args);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // 单次连接失败不影响继续监听
                }
            }
        }, token);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _mutex.Dispose();
    }
}
