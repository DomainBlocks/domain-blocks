using System.Runtime.CompilerServices;
using System.Threading.Channels;
using BenchmarkDotNet.Attributes;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.PostgreSQL;
using DomainBlocks.EventStore.PostgreSQL.Feeds;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.SystemTextJson;

namespace DomainBlocks.EventStore.Benchmarks;

using LiveMessage = SubscriptionMessage<IDomainEvent, string, StreamPosition, LogPosition>;

/// <summary>
/// Measures the PostgreSQL live feed without I/O: a session hands <see cref="EventCount"/> rows to the feed, which
/// fans them out to <see cref="ObserverCount"/> observers, each taking the event and queueing it as a subscription
/// does.
/// </summary>
[MemoryDiagnoser]
public class LiveFeedFanOutBenchmarks
{
    private IEventDecoder<IDomainEvent, PostgresEventData, string> _decoder = null!;
    private EncodedEvent<PostgresEventData, string>[] _rows = null!;
    private EventFilter _filter = null!;

    [Params(1, 4, 16)]
    public int ObserverCount { get; set; }

    [Params(10_000)]
    public int EventCount { get; set; }

    /// <summary>
    /// How many in every hundred events the observers select, by event name. At a hundred they have no filter. The
    /// events that they pass over are never decoded.
    /// </summary>
    [Params(100, 1)]
    public int SelectedPercent { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var codec = EventCodec.Create(new EventCodecOptions<IDomainEvent, PostgresEventData, string>
        {
            TypeMap = EventTypeMap.Create(
                EventTypeMapping.ReadWrite<TestEvent>(),
                EventTypeMapping.ReadWrite<OtherTestEvent>()),
            EventSerializer = new JsonObjectSerializer().AsPostgresEventDataSerializer(),
            MetadataSerializer = new JsonMetadataSerializer()
        });

        var events = TestEvents.Create(SerializationFormat.Json, EventCount, metadataEntryCount: 2, SelectedPercent);

        _decoder = codec;
        _rows = codec.Encode(events).ToArray();
        _filter = SelectedPercent == 100 ? EventFilter.All : EventFilter.EventName(nameof(TestEvent));
    }

    [Benchmark]
    public async Task FanOut_NoIO()
    {
        var feed = new EventLogFeed<EventLogRow<IDomainEvent>>(_ =>
            Task.FromResult<IEventLogSession<EventLogRow<IDomainEvent>>>(new Session(_decoder, _rows)));

        var observers = new Observer[ObserverCount];
        var attachments = new IDisposable[ObserverCount];

        for (var i = 0; i < observers.Length; i++)
        {
            observers[i] = new Observer(EventCount, _filter);
            attachments[i] = feed.Attach(observers[i]);
        }

        await using (await feed.ConnectAsync())
        {
            await Task.WhenAll(observers.Select(x => x.Completion));
        }

        foreach (var attachment in attachments)
            attachment.Dispose();
    }

    /// <summary>
    /// Yields each row as the replication session does, then stays open, as a live session would.
    /// </summary>
    private sealed class Session(
        IEventDecoder<IDomainEvent, PostgresEventData, string> decoder,
        EncodedEvent<PostgresEventData, string>[] rows) : IEventLogSession<EventLogRow<IDomainEvent>>
    {
        private readonly EventLogRow<IDomainEvent> _row = new(decoder, includeMetadata: true);

        public string Description => "no I/O";

        public async IAsyncEnumerable<EventLogRow<IDomainEvent>> ReadAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var i = 0; i < rows.Length; i++)
            {
                var (eventName, eventData, metadata) = rows[i];

                _row.Set(i, "test-stream", i, eventName, eventData, metadata, NoIOEventStore.CreatedAt);
                yield return _row;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Queues what it is given, as the observer of a subscription does, and completes once it has had it all. It takes
    /// each item off the queue again at once, in place of a subscriber on another thread, so that a run does the same
    /// work every time.
    /// </summary>
    private sealed class Observer(int expectedCount, EventFilter filter) :
        IEventLogObserver<EventLogRow<IDomainEvent>>
    {
        private readonly Channel<LiveMessage> _channel = Channel.CreateBounded<LiveMessage>(
            new BoundedChannelOptions(SubscriptionOptions.Default.QueueCapacity)
            {
                SingleWriter = true,
                SingleReader = true
            });

        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _count;

        public Task Completion => _completion.Task;

        public ValueTask OnNextAsync(EventLogRow<IDomainEvent> row, CancellationToken cancellationToken)
        {
            if (filter.Matches(row) &&
                (!_channel.Writer.TryWrite(SubscriptionMessage.Event(row.DecodedEvent)) ||
                 !_channel.Reader.TryRead(out _)))
            {
                _completion.TrySetException(new InvalidOperationException("The queue did not take the event."));
            }

            if (++_count == expectedCount)
                _completion.TrySetResult();

            return ValueTask.CompletedTask;
        }

        public ValueTask OnResetAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken)
        {
            _completion.TrySetException(exception);
            return ValueTask.CompletedTask;
        }
    }
}