using System.Collections.Concurrent;
using System.Threading.Channels;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Serialization;
using DomainBlocks.EventStore.MongoDB.Coordination;
using DomainBlocks.EventStore.MongoDB.Schema;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB;

public class MongoEventStoreClient<TEvent>(
    ChannelWriter<BsonDocument> requestWriter,
    EventCodec<TEvent, BsonValue, BsonValue> eventCodec,
    ILogger<MongoEventStoreClient<TEvent>> logger) :
    IEventStoreClient<TEvent>,
    ICommitObserver
    where TEvent : notnull
{
    private readonly IEventEncoder<TEvent, BsonValue, BsonValue> _eventEncoder = eventCodec.Encoder;
    private readonly IEventDecoder<TEvent, BsonValue, BsonValue> _eventDecoder = eventCodec.Decoder;
    private readonly ILogger<MongoEventStoreClient<TEvent>> _logger = logger;
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _pendingCommits = [];

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<AppendEvent<TEvent>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AppendToStreamOptions();

        var eventsArray = new BsonArray(
            _eventEncoder
                .Encode(events)
                .Select(x => new BsonDocument
                {
                    { PendingEvent.FieldNames.EventName, x.EventName },
                    { PendingEvent.FieldNames.EventData, x.EventData },
                    { PendingEvent.FieldNames.Metadata, x.Metadata ?? BsonNull.Value }
                }));

        var request = new BsonDocument
        {
            { AppendRequest.FieldNames.CommitId, new BsonBinaryData(options.CommitId, GuidRepresentation.Standard) },
            { AppendRequest.FieldNames.StreamId, streamId },
            { AppendRequest.FieldNames.ExpectedStreamState, BsonDocument.From(options.ExpectedState) },
            { AppendRequest.FieldNames.Events, eventsArray },
            { AppendRequest.FieldNames.CreatedAtUtc, DateTime.UtcNow }
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.Timeout);

        var tcs = _pendingCommits.GetOrAdd(
            options.CommitId,
            _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        try
        {
            await requestWriter.WriteAsync(request, timeoutCts.Token);
            await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller canceled.
            throw;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"Append request did not complete within {options.Timeout}.");
        }
    }

    public IAsyncEnumerable<ReadEvent<TEvent>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    void ICommitObserver.OnCommitted(Guid commitId)
    {
        if (_pendingCommits.TryRemove(commitId, out var tcs))
            tcs.TrySetResult();
    }

    void ICommitObserver.OnConflictRejected(Guid commitId, BsonValue conflict)
    {
        if (!_pendingCommits.TryRemove(commitId, out var tcs))
            return;

        var streamId = conflict[AppendConflict.FieldNames.StreamId].AsString;
        var expectedStreamState = conflict[AppendConflict.FieldNames.ExpectedStreamState].ToExpectedStreamState();
        var actualStreamState = conflict[AppendConflict.FieldNames.ActualStreamState].ToStreamState();

        tcs.TrySetException(new StreamAppendConflictException(streamId, expectedStreamState, actualStreamState));
    }
}