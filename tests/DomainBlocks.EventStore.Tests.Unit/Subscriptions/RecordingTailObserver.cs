using System.Threading.Channels;
using DomainBlocks.EventStore.Subscriptions;

namespace DomainBlocks.EventStore.Tests.Unit.Subscriptions;

/// <summary>
/// Records the log positions of the events that a tail hands to it, and the error.
/// </summary>
internal sealed class RecordingTailObserver : ITailObserver<TestSourceEvent>
{
    private readonly Channel<long> _events = Channel.CreateUnbounded<long>();

    private readonly TaskCompletionSource<Exception> _errorTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<Exception> Error => _errorTcs.Task;

    public void OnNext(TestSourceEvent sourceEvent)
    {
        _events.Writer.TryWrite((long)sourceEvent.Context.LogPosition.Value);
    }

    public void OnError(Exception error) => _errorTcs.TrySetResult(error);

    public async Task<long[]> ReadAsync(int count, CancellationToken cancellationToken)
    {
        return await _events.Reader.ReadAllAsync(cancellationToken).Take(count).ToArrayAsync(cancellationToken);
    }
}