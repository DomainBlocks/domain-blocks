using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.Testing.Integration.Benchmarking;

/// <summary>
/// An event store whose appends do nothing beyond a thread-pool hop, as a real store's do when its append loop
/// completes the caller from another thread. Benchmarking it measures the ceiling of the benchmark harness itself,
/// which is the yardstick for every real store's result: a store figure close to the no-op figure is a harness limit,
/// not a store limit.
/// </summary>
public sealed class NoOpEventStore : IEventStore<object, string, StreamPosition, LogPosition>
{
    public Task AppendAsync(
        string streamId,
        IEnumerable<AppendableEvent<object>> events,
        ExpectedStreamState<StreamPosition>? expectedState = null,
        Guid? commitId = null,
        AppendOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return YieldAsync();
    }

    public IAsyncEnumerable<ReadEvent<object, string, StreamPosition, LogPosition>> ReadAll(
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<LogPosition>? origin = null,
        ReadAllOptions? options = null)
    {
        throw new NotSupportedException();
    }

    public IAsyncEnumerable<ReadEvent<object, string, StreamPosition, LogPosition>> ReadStream(
        string streamId,
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<StreamPosition>? origin = null,
        ReadStreamOptions? options = null)
    {
        throw new NotSupportedException();
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToAll(
        SubscriptionOrigin<LogPosition>? origin = null,
        SubscriptionOptions? options = null)
    {
        throw new NotSupportedException();
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToStream(
        string streamId,
        SubscriptionOrigin<StreamPosition>? origin = null,
        SubscriptionOptions? options = null)
    {
        throw new NotSupportedException();
    }

    private static async Task YieldAsync() => await Task.Yield();
}
