using System.Runtime.CompilerServices;

namespace DomainBlocks.EventStore.Tests.Unit.Decoration;

/// <summary>
/// An in-memory stand-in for a store: snapshots what is appended during the append, as a real store encodes it,
/// and replays configured read events and subscription messages.
/// </summary>
internal sealed class FakeEventStore : IEventStore<object, string, StreamPosition, LogPosition>
{
    public sealed record AppendedEvent(object Payload, KeyValuePair<string, string>[] Metadata);

    public List<(string StreamId, AppendedEvent[] Events)> Appends { get; } = [];

    public IEnumerable<AppendableEvent<object>>? LastAppendedEnumerable { get; private set; }

    public List<ReadEvent<object, string, StreamPosition, LogPosition>> ReadEvents { get; } = [];

    public List<SubscriptionMessage<object, string, StreamPosition, LogPosition>> SubscriptionMessages { get; } = [];

    public bool Disposed { get; private set; }

    public int InitializeCalls { get; private set; }

    public Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        InitializeCalls++;
        return Task.CompletedTask;
    }

    public Task AppendAsync(
        string streamId,
        IEnumerable<AppendableEvent<object>> events,
        ExpectedStreamState<StreamPosition> expectedState = default,
        Guid? commitId = null,
        AppendOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var eventArray = events as AppendableEvent<object>[] ?? [.. events];
        LastAppendedEnumerable = eventArray;
        Appends.Add((streamId, [.. eventArray.Select(e => new AppendedEvent(e.Payload, [.. e.Metadata]))]));
        return Task.CompletedTask;
    }

    public IAsyncEnumerable<ReadEvent<object, string, StreamPosition, LogPosition>> ReadAll(
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<LogPosition> origin = default,
        ReadAllOptions? options = null)
    {
        return Replay(ReadEvents);
    }

    public IAsyncEnumerable<ReadEvent<object, string, StreamPosition, LogPosition>> ReadStream(
        string streamId,
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<StreamPosition> origin = default,
        ReadStreamOptions? options = null)
    {
        return Replay(ReadEvents);
    }

    public IAsyncEnumerable<SubscriptionMessage<object, string, StreamPosition, LogPosition>> SubscribeToAll(
        SubscriptionOrigin<LogPosition> origin = default,
        SubscriptionOptions? options = null)
    {
        return Replay(SubscriptionMessages);
    }

    public IAsyncEnumerable<SubscriptionMessage<object, string, StreamPosition, LogPosition>> SubscribeToStream(
        string streamId,
        SubscriptionOrigin<StreamPosition> origin = default,
        SubscriptionOptions? options = null)
    {
        return Replay(SubscriptionMessages);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    public static ReadEvent<object, string, StreamPosition, LogPosition> ReadEventAt(
        object payload,
        ulong position,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        var context = ReadEventContext.Create(
            "stream-1",
            payload.GetType().Name,
            metadata ?? new Dictionary<string, string>(),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(position),
            new StreamPosition(position),
            new LogPosition(position));

        return ReadEvent.Create(payload, context);
    }

    private static async IAsyncEnumerable<T> Replay<T>(
        IEnumerable<T> items,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return item;
        }
    }
}