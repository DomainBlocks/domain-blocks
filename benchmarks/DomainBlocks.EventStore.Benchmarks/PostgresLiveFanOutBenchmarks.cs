using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.PostgreSQL;
using DomainBlocks.EventStore.PostgreSQL.Feeds;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.SystemTextJson;

namespace DomainBlocks.EventStore.Benchmarks;

using LiveEvent = ReadEvent<IDomainEvent, string, StreamPosition, LogPosition>;
using SubscriptionObserver = SubscriptionAsyncEnumerable<IDomainEvent, LogPosition>.Observer;

/// <summary>
/// Measures the PostgreSQL live path without I/O: a session hands <see cref="EventCount"/> events to the feed, which
/// fans them out to the observers of <see cref="SubscriberCount"/> subscriptions. The feed and the observers are the
/// real ones. The session stands in for the replication session, and does with each row what that does.
/// </summary>
[MemoryDiagnoser]
public class PostgresLiveFanOutBenchmarks
{
    private IEventDecoder<IDomainEvent, PostgresEventData, string> _decoder = null!;
    private EncodedEvent<PostgresEventData, string>[] _rows = null!;

    [Params(1, 4, 16)]
    public int SubscriberCount { get; set; }

    [Params(10_000)]
    public int EventCount { get; set; }

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
    }

    [Benchmark]
    public async Task<int> FanOut_NoIO()
    {
        var feed = new EventLogFeed<LiveEvent>(_ =>
            Task.FromResult<IEventLogSession<LiveEvent>>(new Session(_decoder, _rows)));

        var subscribers = new Subscriber[SubscriberCount];
        var attachments = new IDisposable[SubscriberCount];

        for (var i = 0; i < subscribers.Length; i++)
        {
            subscribers[i] = new Subscriber(EventCount);
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
    /// Decodes each row and yields the event, as the replication session does, then stays open, as a live session would.
    /// </summary>
    private sealed class Session(
        IEventDecoder<IDomainEvent, PostgresEventData, string> decoder,
        EncodedEvent<PostgresEventData, string>[] rows) : IEventLogSession<LiveEvent>
    {
        public string Description => "no I/O";

        public async IAsyncEnumerable<LiveEvent> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var i = 0; i < rows.Length; i++)
            {
                var (eventName, eventData, metadata) = rows[i];

                yield return decoder.Decode(
                    i,
                    "test-stream",
                    i,
                    eventName,
                    eventData,
                    metadata,
                    NoIOEventStore.CreatedAt);
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Stands in for a subscription. It passes each event to the observer that a subscription attaches to the feed, and
    /// takes whatever the observer queued straight off the queue again, in place of a subscriber reading on another
    /// thread, so that a run does the same work every time.
    /// </summary>
    private sealed class Subscriber(int expectedCount) : IEventLogObserver<LiveEvent>, IDisposable
    {
        private readonly SubscriptionObserver _observer = new(SubscriptionOptions.Default.QueueCapacity);
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _offeredCount;

        public Task Completion => _completion.Task;

        public int DeliveredCount { get; private set; }

        public ValueTask OnNextAsync(LiveEvent e, CancellationToken cancellationToken)
        {
            // The observer never waits.
            _observer.OnNextAsync(e, cancellationToken).GetAwaiter().GetResult();

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