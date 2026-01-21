using System.Runtime.InteropServices;
using System.Threading.Channels;
using DomainBlocks.EventStore.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Appender.Host;

using EncodedEvent = EncodedEvent<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>;

public class EventAppender
{
    private const string GlobalPositionSequenceId = "global_position";

    private readonly SequenceAllocator _sequenceAllocator;
    private readonly IMongoCollection<Schema.StreamCommit> _commitsCollection;
    private readonly Channel<AppendWorkItem> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _consumeTask;

    public EventAppender(IMongoClient mongoClient, IOptions<EventAppenderOptions> options)
    {
        var db = mongoClient.GetDatabase(options.Value.Mongo.DatabaseName);

        var sequencesCollection = db
            .GetCollection<BsonDocument>(options.Value.Mongo.SequencesCollectionName)
            .WithWriteConcern(WriteConcern.WMajority.With(journal: true));

        _sequenceAllocator = new SequenceAllocator(sequencesCollection);

        _commitsCollection = db
            .GetCollection<Schema.StreamCommit>(options.Value.Mongo.StreamCommitsCollectionName)
            .WithWriteConcern(WriteConcern.WMajority.With(journal: true));

        var boundedChannelOptions = new BoundedChannelOptions(capacity: options.Value.QueueSize)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true,
            AllowSynchronousContinuations = false
        };

        _channel = Channel.CreateBounded<AppendWorkItem>(boundedChannelOptions);
        _consumeTask = ConsumeAsync();
    }

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<EncodedEvent> events,
        AppendToStreamOptions options,
        CancellationToken cancellationToken = default)
    {
        var expectedState = options.ExpectedState;

        var currentState = await GetStreamStateAsync(streamId, cancellationToken).ConfigureAwait(false);

        if (!expectedState.Matches(currentState))
            throw new StreamAppendConflictException(streamId, expectedState, currentState);

        var startVersion = currentState.IsStreamExists
            ? new StreamVersion(currentState.Version.Value.Value + 1)
            : new StreamVersion(0);

        var eventDocuments = ToEventDocuments(events).ToArray();
        if (eventDocuments.Length == 0)
            return;

        var commit = new Schema.StreamCommit
        {
            StreamId = streamId,
            StartStreamVersion = startVersion.ToInt64(),
            EndStreamVersion = startVersion.ToInt64() + eventDocuments.Length - 1,
            Events = eventDocuments
        };

        var workItem = new AppendWorkItem(commit, cancellationToken);

        await _channel.Writer.WriteAsync(workItem, cancellationToken);

        await workItem.Ack.Task.WaitAsync(cancellationToken);
    }

    private static IEnumerable<Schema.EventDocument> ToEventDocuments(IEnumerable<EncodedEvent> events)
    {
        foreach (var @event in events)
        {
            var (eventName, eventData, metadata) = @event;

            yield return new Schema.EventDocument
            {
                EventName = eventName,
                EventData = ToRawBsonDocument(eventData),
                Metadata = metadata.IsEmpty ? BsonNull.Value : ToRawBsonDocument(metadata)
            };
        }
    }

    private static RawBsonDocument ToRawBsonDocument(ReadOnlyMemory<byte> memory)
    {
        if (!MemoryMarshal.TryGetArray(memory, out var segment) || segment.Array is null)
            return new RawBsonDocument(memory.ToArray());

        // Profile this
        switch (segment.Offset)
        {
            case 0 when segment.Count == segment.Array.Length:
                // Full array: simplest path (driver wraps internally)
                return new RawBsonDocument(segment.Array);
            case 0:
                // Prefix of array: avoid ByteBufferSlice by using length-limited buffer
                return new RawBsonDocument(new ByteArrayBuffer(segment.Array, segment.Count, isReadOnly: true));
            default:
                // True slice: need buffer + slice
                var buffer = new ByteArrayBuffer(segment.Array, isReadOnly: true);
                var slice = new ByteBufferSlice(buffer, segment.Offset, segment.Count);
                return new RawBsonDocument(slice);
        }
    }

    private async Task<StreamState> GetStreamStateAsync(string streamId, CancellationToken cancellationToken)
    {
        var latestVersion = await _commitsCollection
            .Find(Builders<Schema.StreamCommit>.Filter.Eq(x => x.StreamId, streamId))
            .Sort(Builders<Schema.StreamCommit>.Sort.Descending(x => x.EndStreamVersion))
            .Project(x => (long?)x.EndStreamVersion)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return latestVersion.HasValue
            ? StreamState.StreamExists(StreamVersion.FromInt64(latestVersion.Value))
            : StreamState.StreamDoesNotExist;
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var item in _channel.Reader.ReadAllAsync(_cts.Token))
            {
                if (item.CancellationToken.IsCancellationRequested)
                {
                    item.Ack.TrySetCanceled(item.CancellationToken);
                    continue;
                }

                var commit = item.Commit;

                try
                {
                    using var linkedCt = CancellationTokenSource.CreateLinkedTokenSource(
                        _cts.Token,
                        item.CancellationToken);

                    var globalPositionAllocation = await _sequenceAllocator
                        .AllocateNextAsync(GlobalPositionSequenceId, commit.Events.Length, linkedCt.Token)
                        .ConfigureAwait(false);

                    commit.StartGlobalPosition = globalPositionAllocation.Start;
                    commit.EndGlobalPosition = globalPositionAllocation.EndExclusive - 1;
                    commit.CommittedAtUtc = DateTime.UtcNow;

                    await _commitsCollection.InsertOneAsync(commit, cancellationToken: linkedCt.Token);

                    item.Ack.SetResult();
                }
                catch (OperationCanceledException)
                {
                    item.Ack.TrySetCanceled();
                }
                catch (Exception ex)
                {
                    item.Ack.TrySetException(ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal during shutdown
            _channel.Writer.TryComplete();

            while (_channel.Reader.TryRead(out var nextItem))
                nextItem.Ack.TrySetCanceled();
        }
        catch (Exception ex)
        {
            _channel.Writer.TryComplete(ex);

            while (_channel.Reader.TryRead(out var nextItem))
                nextItem.Ack.TrySetException(ex);

            throw;
        }
    }

    private sealed class AppendWorkItem(Schema.StreamCommit commit, CancellationToken cancellationToken)
    {
        public Schema.StreamCommit Commit { get; } = commit;
        public CancellationToken CancellationToken { get; } = cancellationToken;
        public TaskCompletionSource Ack { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}