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
    private Task _notificationLoop = Task.CompletedTask;

    private AppendNotificationListener(NpgsqlConnection connection, string channelName)
    {
        _connection = connection;

        _connection.Notification += (_, e) =>
        {
            if (e.Channel == channelName)
                _positions.Writer.TryWrite(long.Parse(e.Payload));
        };
    }

    public static async Task<AppendNotificationListener> StartAsync(
        NpgsqlDataSource dataSource,
        string channelName,
        CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var listener = new AppendNotificationListener(connection, channelName);

        await using (var command = new NpgsqlCommand($"LISTEN \"{channelName}\"", connection))
            await command.ExecuteNonQueryAsync(cancellationToken);

        listener._notificationLoop = listener.WaitForNotificationsAsync();

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
        await _notificationLoop;
        await _connection.DisposeAsync();
        _stopCts.Dispose();
    }

    // A connection raises notifications only while something reads from it. The ones that arrive between waits stay
    // buffered, and each wait raises the next, so none is lost.
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