using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.MongoDB;
using DomainBlocks.EventStore.Subscriptions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.MongoDB.Bson;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.Benchmarks;

/// <summary>
/// Measures the MongoDB live path without I/O: a source hands <see cref="EventCount"/> documents to the tail, which
/// fans them out to the queues of <see cref="SubscriberCount"/> subscriptions, each of which selects the documents that
/// <see cref="Selection"/> says and takes their events. The tail and the queues are the real
/// ones. The source stands in for MongoDB's and reads into one reusable document, as MongoDB's does.
/// </summary>
[MemoryDiagnoser]
public class MongoLiveFanOutBenchmarks
{
    // Long enough that the tail never polls during a run.
    private static readonly EventLogTailOptions TailOptions = new() { PollInterval = TimeSpan.FromHours(1) };

    private IEventDecoder<IDomainEvent, BsonValue, BsonValue> _decoder = null!;
    private BsonDocument[] _documents = null!;
    private EventFilter _filter = null!;

    [Params(1, 4, 16)]
    public int SubscriberCount { get; set; }

    [Params(10_000)]
    public int EventCount { get; set; }

    /// <summary>
    /// How much of the log each subscription selects.
    /// </summary>
    [ParamsAllValues]
    public FanOutSelection Selection { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var codec = EventCodec.Create(new EventCodecOptions<IDomainEvent, BsonValue, BsonValue>
        {
            TypeMap = new EventTypeMapBuilder().Add<TestEvent>().Build(),
            EventSerializer = new BsonDocumentObjectSerializer(),
            MetadataSerializer = new BsonDocumentMetadataSerializer()
        });

        var events = TestEvents.Create(SerializationFormat.Bson, EventCount, metadataEntryCount: 2);

        _decoder = codec;

        _filter = Selection.CreateFilter(EventCount);

        _documents =
        [
            .. codec
                .Encode(events)
                .Select((x, i) => new BsonDocument
                {
                    { EventLogEntry.FieldNames.Position, (long)i },
                    { EventLogEntry.FieldNames.StreamId, FanOutSelections.StreamIdOf(i) },
                    { EventLogEntry.FieldNames.StreamPosition, (long)i },
                    { EventLogEntry.FieldNames.EventName, x.EventName },
                    { EventLogEntry.FieldNames.EventData, x.EventData },
                    { EventLogEntry.FieldNames.Metadata, x.Metadata ?? BsonNull.Value },
                    { EventLogEntry.FieldNames.CreatedAtUtc, NoIOEventStore.CreatedAt.UtcDateTime }
                })
        ];
    }

    [Benchmark]
    public async Task<int> FanOut_NoIO()
    {
        var source = new Source(_decoder, _documents);

        await using var tail = new EventLogTail<
            EventLogDocument<IDomainEvent>,
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

        // The source's read waits until every subscriber has attached, so that each one is offered every document.
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
    /// Starts at an empty log, then, once released, sets its one document to each event's and yields it undecoded, as
    /// MongoDB's source does. Later reads find nothing, and nothing is ever heard.
    /// </summary>
    private sealed class Source(IEventDecoder<IDomainEvent, BsonValue, BsonValue> decoder, BsonDocument[] documents) :
        IEventSubscriptionSource<EventLogDocument<IDomainEvent>, string, StreamPosition, LogPosition>
    {
        private readonly EventLogDocument<IDomainEvent> _document = new(decoder);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _hasRead;

        public void Release() => _released.TrySetResult();

        public ValueTask<LogPosition?> ReadEndLogPositionAsync(CancellationToken cancellationToken)
        {
            return new ValueTask<LogPosition?>((LogPosition?)null);
        }

        public async IAsyncEnumerable<EventLogDocument<IDomainEvent>> ReadAllAsync(
            LogPosition? fromExclusive,
            LogPosition? toInclusive,
            EventFilter filter,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (_hasRead)
                yield break;

            _hasRead = true;
            await _released.Task.WaitAsync(cancellationToken);

            foreach (var document in documents)
            {
                _document.Set(document);
                yield return _document;
            }
        }

        public ValueTask<StreamPosition?> ReadEndStreamPositionAsync(
            string streamId,
            CancellationToken cancellationToken)
        {
            return new ValueTask<StreamPosition?>((StreamPosition?)null);
        }

        public IAsyncEnumerable<EventLogDocument<IDomainEvent>> ReadStreamAsync(
            string streamId,
            StreamPosition? fromExclusive,
            StreamPosition toInclusive,
            EventFilter filter,
            CancellationToken cancellationToken)
        {
            return AsyncEnumerable.Empty<EventLogDocument<IDomainEvent>>();
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
    /// Stands in for a subscription. It passes each document to the queue that a subscription attaches to the tail, and
    /// takes whatever the queue holds straight off it again, in place of a subscriber reading on another thread, so
    /// that a run does the same work every time.
    /// </summary>
    private sealed class Subscriber(int expectedCount, EventFilter filter) :
        ITailObserver<EventLogDocument<IDomainEvent>>,
        IDisposable
    {
        private readonly
            SubscriptionQueue<EventLogDocument<IDomainEvent>, IDomainEvent, string, StreamPosition, LogPosition>
            _queue = new(SubscriptionOptions.Default.QueueCapacity, filter.Matches);

        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _offeredCount;

        public Task Completion => _completion.Task;

        public int DeliveredCount { get; private set; }

        public void OnNext(EventLogDocument<IDomainEvent> sourceEvent)
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