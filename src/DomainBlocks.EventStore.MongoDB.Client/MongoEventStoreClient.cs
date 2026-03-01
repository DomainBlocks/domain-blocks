using System.Diagnostics;
using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using Google.Protobuf;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client;

public class MongoEventStoreClient<TEvent> : IEventStoreClient<TEvent> where TEvent : notnull
{
    private readonly Api.Appender.V0.AppenderService.AppenderServiceClient _appenderClient;
    private readonly IEventEncoder<TEvent, byte[], byte[]> _eventEncoder;
    private readonly IEventDecoder<TEvent, BsonValue, BsonValue> _eventDecoder;
    private readonly IMongoCollection<Schema.StreamCommit> _commitsCollection;

    public MongoEventStoreClient(
        IMongoClient mongoClient,
        Api.Appender.V0.AppenderService.AppenderServiceClient appenderClient,
        IEventEncoder<TEvent, byte[], byte[]> eventEncoder,
        IEventDecoder<TEvent, BsonValue, BsonValue> eventDecoder)
    {
        var db = mongoClient.GetDatabase("domainblocks");

        _appenderClient = appenderClient;
        _eventEncoder = eventEncoder;
        _eventDecoder = eventDecoder;
        _commitsCollection = db.GetCollection<Schema.StreamCommit>("es_stream_commits");
    }

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<AppendEvent<TEvent>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var batch = ToGrpcEventBatch(events);
        var grpcOptions = ToGrpcOptions(options ?? AppendToStreamOptions.Default);

        var request = new Api.Appender.V0.AppendToStreamRequest
        {
            StreamId = streamId,
            Batch = batch,
            Options = grpcOptions,
        };

        await _appenderClient.AppendToStreamAsync(request, cancellationToken: cancellationToken);
    }

    public async IAsyncEnumerable<ReadEvent<TEvent>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? readOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        readOptions ??= ReadStreamOptions.Default;
        var readPosition = readOptions.Position;
        var direction = readOptions.Direction;

        // Edge cases that represent an empty sequence of events.
        if (readPosition.IsStart && direction == StreamReadDirection.Backward ||
            readPosition.IsEnd && direction == StreamReadDirection.Forward)
        {
            if (readOptions.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw &&
                !await StreamExistsAsync(streamId, cancellationToken).ConfigureAwait(false))
            {
                throw new StreamNotFoundException(streamId);
            }

            yield break;
        }

        var filter = Builders<Schema.StreamCommit>.Filter.Eq(x => x.StreamId, streamId);

        if (readPosition.IsSpecificVersion)
        {
            var versionValue = readPosition.Version.Value.ToInt64();

            var versionFilter = direction == StreamReadDirection.Forward
                ? Builders<Schema.StreamCommit>.Filter.Gte(x => x.EndStreamVersion, versionValue)
                : Builders<Schema.StreamCommit>.Filter.Lte(x => x.StartStreamVersion, versionValue);

            filter = Builders<Schema.StreamCommit>.Filter.And(filter, versionFilter);
        }

        var sort = direction == StreamReadDirection.Forward
            ? Builders<Schema.StreamCommit>.Sort.Ascending(x => x.StartStreamVersion)
            : Builders<Schema.StreamCommit>.Sort.Descending(x => x.StartStreamVersion);

        using var cursor = await _commitsCollection
            .Find(filter)
            .Sort(sort)
            .ToCursorAsync(cancellationToken)
            .ConfigureAwait(false);

        var totalEventCount = 0;
        var yieldedEventCount = 0;
        var startVersionValue = readPosition.Version?.ToInt64();

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var commit in cursor.Current)
            {
                if (commit.Events.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"Commit '{streamId}@v{commit.StartStreamVersion}' contains no events.");
                }

                totalEventCount += commit.Events.Length;

                foreach (var (eventDoc, index) in EnumerateEvents(commit.Events, direction))
                {
                    var versionValue = commit.StartStreamVersion + index;

                    if (startVersionValue.HasValue)
                    {
                        if (direction == StreamReadDirection.Forward && versionValue < startVersionValue.Value)
                            continue;

                        if (direction == StreamReadDirection.Backward && versionValue > startVersionValue.Value)
                            continue;
                    }

                    var (@event, metadata) = _eventDecoder.Decode(
                        eventDoc.EventName,
                        eventDoc.EventData,
                        eventDoc.Metadata);

                    var version = StreamVersion.FromInt64(versionValue);
                    var position = LogPosition.FromInt64(commit.StartGlobalPosition + index);
                    var context = new ReadEventContext(streamId, version, commit.CommittedAtUtc, position);

                    yield return ReadEvent.Create(@event, metadata, context);

                    if (++yieldedEventCount >= readOptions.MaxCount)
                        yield break;
                }
            }
        }

        if (totalEventCount == 0 && readOptions.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw)
            throw new StreamNotFoundException(streamId);

        static IEnumerable<(Schema.EventDocument, int)> EnumerateEvents(
            Schema.EventDocument[] events,
            StreamReadDirection direction)
        {
            if (direction == StreamReadDirection.Forward)
            {
                for (var i = 0; i < events.Length; i++)
                    yield return (events[i], i);
            }
            else
            {
                for (var i = events.Length - 1; i >= 0; i--)
                    yield return (events[i], i);
            }
        }
    }

    private Api.Appender.V0.AppendEventBatch ToGrpcEventBatch(IEnumerable<AppendEvent<TEvent>> events)
    {
        var batch = new Api.Appender.V0.AppendEventBatch
        {
            EventContentType = "application/bson",
            MetadataContentType = "application/bson"
        };

        foreach (var (eventName, eventData, metadata) in _eventEncoder.Encode(events))
        {
            batch.Events.Add(new Api.Appender.V0.AppendEvent
            {
                EventName = eventName,
                EventData = UnsafeByteOperations.UnsafeWrap(eventData),
                Metadata = metadata == null ? ByteString.Empty : UnsafeByteOperations.UnsafeWrap(metadata)
            });
        }

        return batch;
    }

    private static Api.Appender.V0.AppendToStreamOptions ToGrpcOptions(AppendToStreamOptions options)
    {
        var grpcExpectedStateKind = options.ExpectedState.Kind switch
        {
            ExpectedStreamStateKind.Any => Api.Appender.V0.ExpectedStreamStateKind.Any,
            ExpectedStreamStateKind.StreamDoesNotExist => Api.Appender.V0.ExpectedStreamStateKind.StreamDoesNotExist,
            ExpectedStreamStateKind.StreamExists => Api.Appender.V0.ExpectedStreamStateKind.StreamExists,
            ExpectedStreamStateKind.SpecificVersion => Api.Appender.V0.ExpectedStreamStateKind.SpecificVersion,
            _ => throw new UnreachableException($"Unexpected ExpectedStreamStateKind '{options.ExpectedState.Kind}'.")
        };

        var grpcOptions = new Api.Appender.V0.AppendToStreamOptions
        {
            ExpectedState = new Api.Appender.V0.ExpectedStreamState
            {
                Kind = grpcExpectedStateKind
            }
        };

        if (options.ExpectedState.Version?.Value is { } v)
            grpcOptions.ExpectedState.Version = v;

        return grpcOptions;
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

    private async Task<bool> StreamExistsAsync(string streamId, CancellationToken cancellationToken)
    {
        var streamState = await GetStreamStateAsync(streamId, cancellationToken).ConfigureAwait(false);
        return streamState.IsStreamExists;
    }
}