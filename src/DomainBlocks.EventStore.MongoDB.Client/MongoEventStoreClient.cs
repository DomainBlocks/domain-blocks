using System.Threading.Channels;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Coordination;
using DomainBlocks.EventStore.MongoDB.Client.Schema;
using DomainBlocks.EventStore.MongoDB.Client.Serialization;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client;

public class MongoEventStoreClient<TEvent> :
    IEventStoreClient<TEvent>,
    IAsyncDisposable
    where TEvent : notnull
{
    private readonly IMongoCollection<BsonDocument> _requests;

    private readonly IEventEncoder<TEvent, BsonValue, BsonValue> _eventEncoder;
    private readonly IEventDecoder<TEvent, BsonValue, BsonValue> _eventDecoder;
    private readonly ICommitTracker _requestTracker;
    private readonly ILogger<MongoEventStoreClient<TEvent>> _logger;
    private readonly Channel<BsonDocument> _channel;
    private readonly Task _consumeTask;
    private readonly CancellationTokenSource _stopCts = new();

    public MongoEventStoreClient(
        IMongoCollection<BsonDocument> requests,
        ICommitTracker requestTracker,
        MongoEventStoreClientOptions options,
        EventCodec<TEvent, BsonValue, BsonValue> eventCodec,
        ILogger<MongoEventStoreClient<TEvent>> logger)
    {
        _requestTracker = requestTracker;
        _requests = requests.WithWriteConcern(WriteConcern.W1.With(journal: false));
        _eventEncoder = eventCodec.Encoder;
        _eventDecoder = eventCodec.Decoder;
        _logger = logger;

        var channelOptions = new BoundedChannelOptions(options.RequestQueueCapacity)
        {
            SingleWriter = false,
            SingleReader = true
        };

        _channel = Channel.CreateBounded<BsonDocument>(channelOptions);
        _consumeTask = ConsumeRequestsAsync(options.RequestInsertBatchSize, _stopCts.Token);
    }

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<AppendEvent<TEvent>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= AppendToStreamOptions.Default;

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

        var commitTask = _requestTracker.WaitAsync(options.CommitId, timeoutCts.Token);

        try
        {
            await _channel.Writer.WriteAsync(request, timeoutCts.Token);
            await commitTask.ConfigureAwait(false);
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

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await _stopCts.CancelAsync();
        await _consumeTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _stopCts.Dispose();
    }

    private async Task ConsumeRequestsAsync(int insertBatchSize, CancellationToken ct)
    {
        var batch = new List<BsonDocument>(insertBatchSize);

        try
        {
            while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                batch.Clear();

                while (batch.Count < insertBatchSize && _channel.Reader.TryRead(out var request))
                    batch.Add(request);

                _logger.LogDebug("Request batch size: {Count}", batch.Count);

                await _requests.InsertManyAsync(batch, cancellationToken: ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Request consumer cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Request consumer failed");
        }
    }
}