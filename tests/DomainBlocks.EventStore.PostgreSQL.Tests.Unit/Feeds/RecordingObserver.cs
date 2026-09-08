using System.Threading.Channels;
using DomainBlocks.EventStore.PostgreSQL.Feeds;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit.Feeds;

/// <summary>
/// Records what the feed delivers, in order, as readable strings: "row:{position}", "reset" or "error".
/// </summary>
internal sealed class RecordingObserver : IEventLogObserver
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>();
    private readonly TaskCompletionSource<Exception> _errorTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<Exception> Error => _errorTcs.Task;

    public ValueTask OnNextAsync(EventLogRow row, CancellationToken cancellationToken)
    {
        _channel.Writer.TryWrite($"row:{row.Position}");
        return ValueTask.CompletedTask;
    }

    public ValueTask OnResetAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryWrite("reset");
        return ValueTask.CompletedTask;
    }

    public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken)
    {
        _channel.Writer.TryWrite("error");
        _errorTcs.TrySetResult(exception);
        return ValueTask.CompletedTask;
    }

    public async Task<string[]> ReadAsync(int count, CancellationToken cancellationToken)
    {
        return await _channel.Reader.ReadAllAsync(cancellationToken).Take(count).ToArrayAsync(cancellationToken);
    }
}
