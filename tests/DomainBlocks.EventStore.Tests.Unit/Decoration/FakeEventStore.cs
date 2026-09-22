using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Filtering;

namespace DomainBlocks.EventStore.Tests.Unit.Decoration;

/// <summary>
/// An in-memory stand-in for a store: snapshots what is appended during the append, as a real store encodes it,
/// and replays configured read events and subscription messages, those that the filter it is given selects.
/// </summary>
internal sealed class FakeEventStore : IEventStore<object, string, StreamPosition, LogPosition>, IEventFilterExplainer
{
    public sealed record AppendedEvent(object Payload, KeyValuePair<string, string>[] Metadata);

    public List<(string StreamId, AppendedEvent[] Events)> Appends { get; } = [];

    public IEnumerable<AppendableEvent<object>>? LastAppendedEnumerable { get; private set; }

    public List<ReadEvent<object, string, StreamPosition, LogPosition>> ReadEvents { get; } = [];

    public List<SubscriptionMessage<object, string, StreamPosition, LogPosition>> SubscriptionMessages { get; } = [];

    public ReadAllOptions? LastReadAllOptions { get; private set; }

    public ReadStreamOptions? LastReadStreamOptions { get; private set; }

    public SubscriptionOptions? LastSubscriptionOptions { get; private set; }

    public FilterPushdownMode? LastExplainedPushdown { get; private set; }

    public bool Disposed { get; private set; }

    public int InitializeCalls { get; private set; }

    // As a store whose database evaluates the whole of any filter.
    public EventFilterPlan ExplainFilter(EventFilter filter, FilterPushdownMode pushdownMode = FilterPushdownMode.Prefer)
    {
        LastExplainedPushdown = pushdownMode;
        return new EventFilterPlan(filter, EventFilter.All);
    }

    public Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        InitializeCalls++;
        return Task.CompletedTask;
    }

    public Task AppendAsync(
        string streamId,
        IEnumerable<AppendableEvent<object>> events,
        ExpectedStreamState<StreamPosition>? expectedState = null,
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
        ReadOrigin<LogPosition>? origin = null,
        ReadAllOptions? options = null)
    {
        LastReadAllOptions = options;
        return Replay(Select(ReadEvents, options?.Filter, options?.MaxCount, options?.IncludeMetadata ?? true));
    }

    public IAsyncEnumerable<ReadEvent<object, string, StreamPosition, LogPosition>> ReadStream(
        string streamId,
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<StreamPosition>? origin = null,
        ReadStreamOptions? options = null)
    {
        LastReadStreamOptions = options;
        return Replay(Select(ReadEvents, options?.Filter, options?.MaxCount, options?.IncludeMetadata ?? true));
    }

    public IAsyncEnumerable<SubscriptionMessage<object, string, StreamPosition, LogPosition>> SubscribeToAll(
        SubscriptionOrigin<LogPosition>? origin = null,
        SubscriptionOptions? options = null)
    {
        LastSubscriptionOptions = options;
        return Replay(Select(SubscriptionMessages, options?.Filter));
    }

    public IAsyncEnumerable<SubscriptionMessage<object, string, StreamPosition, LogPosition>> SubscribeToStream(
        string streamId,
        SubscriptionOrigin<StreamPosition>? origin = null,
        SubscriptionOptions? options = null)
    {
        LastSubscriptionOptions = options;
        return Replay(Select(SubscriptionMessages, options?.Filter));
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

    // As a store does: the filter is about the event as it is stored, then the count, then the metadata.
    private static IEnumerable<ReadEvent<object, string, StreamPosition, LogPosition>> Select(
        IEnumerable<ReadEvent<object, string, StreamPosition, LogPosition>> events,
        EventFilter? filter,
        int? maxCount,
        bool includeMetadata)
    {
        var subject = new FilterableReadEvent<object, string, StreamPosition, LogPosition>();

        return events
            .Where(x =>
            {
                subject.Set(x);
                return filter?.Matches(subject) ?? true;
            })
            .Take(maxCount ?? int.MaxValue)
            .Select(x => includeMetadata ? x : ReadEventAt(x.Payload, x.Context.LogPosition.Value));
    }

    private static IEnumerable<SubscriptionMessage<object, string, StreamPosition, LogPosition>> Select(
        IEnumerable<SubscriptionMessage<object, string, StreamPosition, LogPosition>> messages,
        EventFilter? filter)
    {
        var subject = new FilterableReadEvent<object, string, StreamPosition, LogPosition>();

        return messages.Where(x =>
        {
            if (x.Event is not { } e)
                return true;

            subject.Set(e);
            return filter?.Matches(subject) ?? true;
        });
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