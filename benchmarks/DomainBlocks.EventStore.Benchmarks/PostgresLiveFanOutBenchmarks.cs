using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.PostgreSQL;
using DomainBlocks.EventStore.PostgreSQL.Feeds;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.SystemTextJson;

namespace DomainBlocks.EventStore.Benchmarks;

using SubscriptionObserver = SubscriptionAsyncEnumerable<IDomainEvent, LogPosition>.Observer;

/// <summary>
/// Measures the PostgreSQL live path without I/O: a session hands <see cref="EventCount"/> rows to the feed, which fans
/// them out to the observers of <see cref="SubscriberCount"/> subscriptions, each of which selects
/// <see cref="SelectedPercent"/> percent of the rows and takes their events. The feed and the observers are the real
/// ones. The session stands in for the replication session and handles each row as the replication session does.
/// </summary>
[MemoryDiagnoser]
public class PostgresLiveFanOutBenchmarks
{
    // The rows are spread evenly over this many streams, so that one stream holds one percent of them.
    private const int StreamCount = 100;

    private IEventDecoder<IDomainEvent, PostgresEventData, string> _decoder = null!;
    private EncodedEvent<PostgresEventData, string>[] _rows = null!;
    private string[] _streamIds = null!;
    private EventFilter _filter = null!;

    [Params(1, 4, 16)]
    public int SubscriberCount { get; set; }

    [Params(10_000)]
    public int EventCount { get; set; }

    /// <summary>
    /// How much of the log each subscription selects: all of it, without a filter, or the one percent in one stream.
    /// </summary>
    [Params(100, 1)]
    public int SelectedPercent { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var codec = EventCodec.Create(new EventCodecOptions<IDomainEvent, PostgresEventData, string>
        {
            TypeMap = new EventTypeMapBuilder().Add<TestEvent>().Build(),
            EventSerializer = new JsonObjectSerializer().AsPostgresEventDataSerializer(),
            MetadataSerializer = new JsonMetadataSerializer()
        });

        var events = TestEvents.Create(SerializationFormat.Json, EventCount, metadataEntryCount: 2);

        _decoder = codec;
        _rows = [.. codec.Encode(events)];
        _streamIds = [.. Enumerable.Range(0, StreamCount).Select(i => $"test-stream-{i}")];

        _filter = SelectedPercent switch
        {
            100 => EventFilter.All,
            1 => EventFilter.StreamIds(_streamIds[0]),
            _ => throw new NotSupportedException($"No filter selects {SelectedPercent} percent of the rows.")
        };
    }

    [Benchmark]
    public async Task<int> FanOut_NoIO()
    {
        var feed = new EventLogFeed<EventLogRow<IDomainEvent>>(_ =>
            Task.FromResult<IEventLogSession<EventLogRow<IDomainEvent>>>(new Session(_decoder, _rows, _streamIds)));

        var subscribers = new Subscriber[SubscriberCount];
        var attachments = new IDisposable[SubscriberCount];

        for (var i = 0; i < subscribers.Length; i++)
        {
            subscribers[i] = new Subscriber(EventCount, _filter);
            attachments[i] = feed.Attach(subscribers[i]);
        }

        await using (await feed.ConnectAsync())
        {
            await Task.WhenAll(subscribers.Select(x => x.Completion));
        }

        var deliveredCount = 0;

        for (var i = 0; i < subscribers.Length; i++)
        {
            attachments[i].Dispose();
            subscribers[i].Dispose();
            deliveredCount += subscribers[i].DeliveredCount;
        }

        return deliveredCount;
    }

    /// <summary>
    /// Sets its one row for each insert and yields it undecoded, as the replication session does, then stays open, as a
    /// live session would.
    /// </summary>
    private sealed class Session(
        IEventDecoder<IDomainEvent, PostgresEventData, string> decoder,
        EncodedEvent<PostgresEventData, string>[] rows,
        string[] streamIds) : IEventLogSession<EventLogRow<IDomainEvent>>
    {
        private readonly EventLogRow<IDomainEvent> _row = new(decoder);

        public string Description => "no I/O";

        public async IAsyncEnumerable<EventLogRow<IDomainEvent>> ReadAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var i = 0; i < rows.Length; i++)
            {
                var (eventName, eventData, metadata) = rows[i];

                var streamId = streamIds[i % streamIds.Length];

                _row.Set(i, streamId, i, eventName, eventData, metadata, NoIOEventStore.CreatedAt);

                yield return _row;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Stands in for a subscription. It passes each row to the observer that a subscription attaches to the feed, and
    /// takes whatever the observer queued straight off the queue again, in place of a subscriber reading on another
    /// thread, so that a run does the same work every time.
    /// </summary>
    private sealed class Subscriber(int expectedCount, EventFilter filter) :
        IEventLogObserver<EventLogRow<IDomainEvent>>,
        IDisposable
    {
        private readonly SubscriptionObserver _observer = new(SubscriptionOptions.Default.QueueCapacity, filter);

        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _offeredCount;

        public Task Completion => _completion.Task;

        public int DeliveredCount { get; private set; }

        public ValueTask OnNextAsync(EventLogRow<IDomainEvent> row, CancellationToken cancellationToken)
        {
            // The observer never waits.
            _observer.OnNextAsync(row, cancellationToken).GetAwaiter().GetResult();

            if (_observer.Reader.TryRead(out _))
                DeliveredCount++;

            if (++_offeredCount == expectedCount)
                _completion.TrySetResult();

            return ValueTask.CompletedTask;
        }

        public ValueTask OnResetAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken)
        {
            _completion.TrySetException(exception);
            return ValueTask.CompletedTask;
        }

        public void Dispose() => _observer.Dispose();
    }
}