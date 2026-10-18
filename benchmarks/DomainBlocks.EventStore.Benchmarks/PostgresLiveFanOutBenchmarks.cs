using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.PostgreSQL;
using DomainBlocks.EventStore.Subscriptions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.SystemTextJson;

namespace DomainBlocks.EventStore.Benchmarks;

/// <summary>
/// Measures the PostgreSQL live path without I/O: a source hands <see cref="EventCount"/> rows to the tail, which fans
/// them out to the queues of <see cref="SubscriberCount"/> subscriptions, each of which selects
/// <see cref="SelectedPercent"/> percent of the rows and takes their events. The real implementations are used for the
/// tail and queues. The source is a stub without I/O that replaces
/// <see cref="PostgresSubscriptionSource{TEvent}"/>, and reads into one reusable row, as the real implementation does.
/// </summary>
[MemoryDiagnoser]
public class PostgresLiveFanOutBenchmarks
{
    // The rows are spread evenly over this many streams, so that one stream holds one percent of them.
    private const int StreamCount = 100;

    // Long enough that the tail never polls during a run.
    private static readonly EventLogTailOptions TailOptions = new() { PollInterval = TimeSpan.FromHours(1) };

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
        var source = new Source(_decoder, _rows, _streamIds);

        await using var tail = new EventLogTail<
            EventLogRow<IDomainEvent>,
            IDomainEvent,
            string,
            StreamPosition,
            LogPosition>(() => source, TailOptions);

        var subscribers = new Subscriber[SubscriberCount];
        var attachments = new IAsyncDisposable[SubscriberCount];

        for (var i = 0; i < subscribers.Length; i++)
        {
            subscribers[i] = new Subscriber(EventCount, _filter);
            attachments[i] = await tail.AttachAsync(subscribers[i]);
        }

        // The source's read waits until every subscriber has attached, so that each one is offered every row.
        source.Release();
        await Task.WhenAll(subscribers.Select(x => x.Completion));

        var deliveredCount = 0;

        for (var i = 0; i < subscribers.Length; i++)
        {
            await attachments[i].DisposeAsync();
            subscribers[i].Dispose();
            deliveredCount += subscribers[i].DeliveredCount;
        }

        return deliveredCount;
    }

    /// <summary>
    /// Starts at an empty log, then, once released, sets its one row for each event and yields it undecoded, as
    /// PostgreSQL's source does. Later reads find nothing, and nothing is ever heard.
    /// </summary>
    private sealed class Source(
        IEventDecoder<IDomainEvent, PostgresEventData, string> decoder,
        EncodedEvent<PostgresEventData, string>[] rows,
        string[] streamIds) :
        IEventSubscriptionSource<EventLogRow<IDomainEvent>, string, StreamPosition, LogPosition>
    {
        private readonly EventLogRow<IDomainEvent> _row = new(decoder);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _hasRead;

        public void Release() => _released.TrySetResult();

        public ValueTask<LogPosition?> ReadEndLogPositionAsync(CancellationToken cancellationToken)
        {
            return new ValueTask<LogPosition?>((LogPosition?)null);
        }

        public async IAsyncEnumerable<EventLogRow<IDomainEvent>> ReadAllAsync(
            LogPosition? fromExclusive,
            LogPosition? toInclusive,
            EventFilter filter,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (_hasRead)
                yield break;

            _hasRead = true;
            await _released.Task.WaitAsync(cancellationToken);

            for (var i = 0; i < rows.Length; i++)
            {
                var (eventName, eventData, metadata) = rows[i];
                var streamId = streamIds[i % streamIds.Length];

                _row.Set(i, streamId, i, eventName, eventData, metadata, NoIOEventStore.CreatedAt);

                yield return _row;
            }
        }

        public ValueTask<StreamPosition?> ReadEndStreamPositionAsync(
            string streamId,
            CancellationToken cancellationToken)
        {
            return new ValueTask<StreamPosition?>((StreamPosition?)null);
        }

        public IAsyncEnumerable<EventLogRow<IDomainEvent>> ReadStreamAsync(
            string streamId,
            StreamPosition? fromExclusive,
            StreamPosition toInclusive,
            EventFilter filter,
            CancellationToken cancellationToken)
        {
            return AsyncEnumerable.Empty<EventLogRow<IDomainEvent>>();
        }

        public async IAsyncEnumerable<LogPosition> ListenForAppendsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield break;
        }

        public bool IsTransientError(Exception exception) => false;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Stands in for a subscription. It passes each row to a queue like the one a subscription attaches to the tail.
    /// It then takes whatever the queue holds straight off it again, instead of a subscriber reading on another thread.
    /// That way, every run does the same work.
    /// </summary>
    private sealed class Subscriber(int expectedCount, EventFilter filter) :
        ITailObserver<EventLogRow<IDomainEvent>>,
        IDisposable
    {
        private readonly
            SubscriptionQueue<EventLogRow<IDomainEvent>, IDomainEvent, string, StreamPosition, LogPosition> _queue =
                new(SubscriptionOptions.Default.QueueCapacity, filter.Matches);

        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _offeredCount;

        public Task Completion => _completion.Task;

        public int DeliveredCount { get; private set; }

        public void OnNext(EventLogRow<IDomainEvent> sourceEvent)
        {
            _queue.OnNext(sourceEvent);

            if (_queue.Reader.TryRead(out _))
                DeliveredCount++;

            if (++_offeredCount == expectedCount)
                _completion.TrySetResult();
        }

        public void OnError(Exception error) => _completion.TrySetException(error);

        public void Dispose() => _queue.Dispose();
    }
}