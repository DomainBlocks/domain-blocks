using System.Net;
using System.Net.Sockets;

namespace DomainBlocks.Testing.Integration.EventStore.PostgreSQL;

/// <summary>
/// Forwards local connections to a target that can be moved, as a virtual IP in front of a primary is. Moving it ends
/// the connections to the old target.
/// </summary>
internal sealed class TcpProxy : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Lock _lock = new();
    private readonly Task _accepting;
    private DnsEndPoint _target;
    private CancellationTokenSource _connections = new();

    public TcpProxy(DnsEndPoint target)
    {
        _target = target;
        _listener.Start();
        _accepting = AcceptAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void MoveTo(DnsEndPoint target)
    {
        CancellationTokenSource connections;

        lock (_lock)
        {
            _target = target;
            connections = _connections;
            _connections = new CancellationTokenSource();
        }

        connections.Cancel();
        connections.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _listener.Stop();

        try
        {
            await _accepting;
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }

        lock (_lock)
            _connections.Cancel();
    }

    private async Task AcceptAsync()
    {
        while (true)
        {
            var client = await _listener.AcceptTcpClientAsync(_stopping.Token);

            lock (_lock)
                _ = ForwardAsync(client, _target, _connections.Token);
        }
    }

    // A connection that can't reach the target is closed at once, as a refused connection would be.
    private static async Task ForwardAsync(TcpClient client, DnsEndPoint target, CancellationToken cancellationToken)
    {
        using var server = new TcpClient();

        using (client)
        {
            try
            {
                await server.ConnectAsync(target.Host, target.Port, cancellationToken);

                await Task.WhenAny(
                    client.GetStream().CopyToAsync(server.GetStream(), cancellationToken),
                    server.GetStream().CopyToAsync(client.GetStream(), cancellationToken));
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or IOException)
            {
            }
        }
    }
}