using System.Threading.Channels;
using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// Listens on a channel with a connection of its own, and records the positions that append notifications carry.
/// </summary>
internal sealed class AppendNotificationListener : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly Channel<long> _positions = Channel.CreateUnbounded<long>();
    private readonly CancellationTokenSource _stopCts = new();
    private Task _waiting = Task.CompletedTask;

    private AppendNotificationListener(NpgsqlConnection connection, string channel)
    {
        _connection = connection;

        _connection.Notification += (_, e) =>
        {
            if (e.Channel == channel)
                _positions.Writer.TryWrite(long.Parse(e.Payload));
        };
    }

    public static async Task<AppendNotificationListener> StartAsync(
        NpgsqlDataSource dataSource,
        string channel,
        CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var listener = new AppendNotificationListener(connection, channel);

        await using (var command = new NpgsqlCommand($"LISTEN \"{channel}\"", connection))
            await command.ExecuteNonQueryAsync(cancellationToken);

        listener._waiting = listener.WaitForNotificationsAsync();

        return listener;
    }

    /// <summary>
    /// Waits for the next notification, and returns the position it carries.
    /// </summary>
    public async Task<long> NextAsync(CancellationToken cancellationToken)
    {
        return await _positions.Reader.ReadAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _stopCts.CancelAsync();
        await _waiting;
        await _connection.DisposeAsync();
        _stopCts.Dispose();
    }

    // A connection raises notifications only while something reads from it.
    private async Task WaitForNotificationsAsync()
    {
        try
        {
            while (true)
                await _connection.WaitAsync(_stopCts.Token);
        }
        catch (OperationCanceledException) when (_stopCts.IsCancellationRequested)
        {
        }
    }
}