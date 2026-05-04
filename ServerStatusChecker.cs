using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace BritanniaReborn;

// Polling TCP al puerto del shard para mostrar estado online/offline en la
// pantalla login. Cada N segundos hace un connect; si abre, online; si no
// (timeout/refused), offline. Útil cuando el server reinicia y quieres saber
// cuándo vuelve sin spamear el botón Play.
internal sealed class ServerStatusChecker : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly int _intervalMs;
    private readonly int _timeoutMs;
    private readonly CancellationTokenSource _cts = new();
    private bool? _ultimoEstado;

    public event Action<bool>? StatusChanged;

    public ServerStatusChecker(string host, int port, int intervalMs, int timeoutMs)
    {
        _host = host;
        _port = port;
        _intervalMs = intervalMs;
        _timeoutMs = timeoutMs;
    }

    public void Start()
    {
        _ = Task.Run(LoopAsync);
    }

    private async Task LoopAsync()
    {
        var token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            var online = await CheckAsync(token);
            if (_ultimoEstado != online)
            {
                _ultimoEstado = online;
                try { StatusChanged?.Invoke(online); } catch { }
            }
            try { await Task.Delay(_intervalMs, token); } catch { return; }
        }
    }

    private async Task<bool> CheckAsync(CancellationToken outerToken)
    {
        using var tcp = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
        timeoutCts.CancelAfter(_timeoutMs);
        try
        {
            await tcp.ConnectAsync(_host, _port, timeoutCts.Token);
            return tcp.Connected;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); _cts.Dispose(); } catch { }
    }
}
