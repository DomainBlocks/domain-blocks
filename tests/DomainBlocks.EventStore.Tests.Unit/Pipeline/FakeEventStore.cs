using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.Tests.Unit.Pipeline;

/// <summary>
/// An in-memory stand-in for a store: records what is appended (materialized at append time, as a real store
/// would) and replays configured read events and subscription messages.
/// </summary>
internal sealed class FakeEventStore : IEventStore<object, string, StreamPosition, LogPosition>, IAsyncDisposable
{
    public List<(string StreamId, AppendableEvent<object>[] Events)> Appends { get; } = [];

    public IEnumerable<AppendableEvent<object>>? LastAppendedEnumerable { get; private set; }

    public List<ReadEvent<object, string, StreamPosition, LogPosition>> ReadEvents { get; } = [];

    public List<SubscriptionMessage> SubscriptionMessages { get; } = [];

    public bool Disposed { get; private set; }

    public Task AppendAsync(
        string streamId,
        IEnumerable<AppendableEvent<object>> events,
        ExpectedStreamState<StreamPosition>? expectedState = null,
        Guid? commitId = null,
        AppendOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        LastAppendedEnumerable = events;
        Appends.Add((streamId, events.ToArray()));
        return Task.CompletedTask;
    }

    public IAsyncEnumerable<ReadEvent<object, string, StreamPosition, LogPosition>> ReadAll(
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<LogPosition>? origin = null,
        ReadAllOptions? options = null)
    {
        return Replay(ReadEvents);
    }

    public IAsyncEnumerable<ReadEvent<object, string, StreamPosition, LogPosition>> ReadStream(
        string streamId,
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<StreamPosition>? origin = null,
        ReadStreamOptions? options = null)
    {
        return Replay(ReadEvents);
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToAll(
        SubscriptionOrigin<LogPosition>? origin = null,
        SubscriptionOptions? options = null)
    {
        return Replay(SubscriptionMessages);
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToStream(
        string streamId,
        SubscriptionOrigin<StreamPosition>? origin = null,
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
            await Task.Yield();
            yield return item;
        }
    }
}