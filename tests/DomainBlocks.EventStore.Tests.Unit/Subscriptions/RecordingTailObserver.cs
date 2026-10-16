using System.Threading.Channels;
using DomainBlocks.EventStore.Subscriptions;

namespace DomainBlocks.EventStore.Tests.Unit.Subscriptions;

/// <summary>
/// Records the events and the error that a tail hands to it.
/// </summary>
internal sealed class RecordingTailObserver : ITailObserver<long>
{
    private readonly Channel<long> _events = Channel.CreateUnbounded<long>();

    private readonly TaskCompletionSource<Exception> _errorTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<Exception> Error => _errorTcs.Task;

    public void OnNext(long sourceEvent) => _events.Writer.TryWrite(sourceEvent);

    public void OnError(Exception error) => _errorTcs.TrySetResult(error);

    public async Task<long[]> ReadAsync(int count, CancellationToken cancellationToken)
    {
        return await _events.Reader.ReadAllAsync(cancellationToken).Take(count).ToArrayAsync(cancellationToken);
    }
}