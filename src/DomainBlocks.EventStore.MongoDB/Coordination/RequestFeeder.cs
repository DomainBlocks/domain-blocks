using System.Threading.Channels;
using DomainBlocks.EventStore.MongoDB.ChangeStreams;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

internal sealed class RequestFeeder : IChangeStreamObserver<ChangeStreamDocument<BsonDocument>>, IAsyncDisposable
{
    private readonly IMongoCollection<BsonDocument> _requests;
    private readonly IChangeStreamSubject<ChangeStreamDocument<BsonDocument>> _changeStreamSubject;
    private readonly int _batchSize;
    private readonly Channel<BsonDocument> _catchUpChannel;
    private readonly Channel<BsonDocument> _liveChannel;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopCts = new();
    private IDisposable? _changeStreamAttachment;
    private Task? _catchUpTask;
    private Task? _pumpTask;

    public RequestFeeder(
        IMongoCollection<BsonDocument> requests,
        IChangeStreamSubject<ChangeStreamDocument<BsonDocument>> changeStreamSubject,
        int queueCapacity,
        int batchSize,
        ILogger<RequestFeeder> logger)
    {
        _requests = requests;
        _changeStreamSubject = changeStreamSubject;
        _batchSize = batchSize;
        _logger = logger;

        var channelOptions = new BoundedChannelOptions(queueCapacity)
        {
            SingleWriter = true,
            SingleReader = true
        };

        _catchUpChannel = Channel.CreateBounded<BsonDocument>(channelOptions);
        _liveChannel = Channel.CreateBounded<BsonDocument>(channelOptions);
    }

    public void Start(ChannelWriter<BsonDocument> output)
    {
        // Attach before catch-up so live inserts are buffered immediately.
        _changeStreamAttachment = _changeStreamSubject.Attach(this);
        _catchUpTask = CatchUpAsync(_stopCts.Token);
        _pumpTask = PumpAsync(output, _stopCts.Token);
    }

    async ValueTask IChangeStreamObserver<ChangeStreamDocument<BsonDocument>>.OnNextAsync(
        ChangeStreamDocument<BsonDocument> change,
        CancellationToken cancellationToken)
    {
        if (!change.CollectionNamespace.Equals(_requests.CollectionNamespace) ||
            change.OperationType is not ChangeStreamOperationType.Insert)
        {
            return;
        }

        await _liveChannel.Writer.WriteAsync(change.FullDocument, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        // 1. Stop receiving live events.
        _changeStreamAttachment?.Dispose();

        using (_stopCts)
        {
            // 2. Cancel both tasks - unblocks any pending WriteAsync / ReadAllAsync.
            await _stopCts.CancelAsync().ConfigureAwait(false);

            // 3. Complete source channels so ReadAllAsync in the pump can unblock cleanly.
            _catchUpChannel.Writer.TryComplete();
            _liveChannel.Writer.TryComplete();

            if (_catchUpTask is not null)
                await _catchUpTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            if (_pumpTask is not null)
                await _pumpTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private async Task CatchUpAsync(CancellationToken ct)
    {
        _logger.LogInformation("Catch-up phase starting");

        var sort = Builders<BsonDocument>.Sort.Ascending("_id");
        BsonValue? lastSeenId = null;

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var filter = lastSeenId is not null
                    ? Builders<BsonDocument>.Filter.Gt("_id", lastSeenId)
                    : FilterDefinition<BsonDocument>.Empty;

                var batch = await _requests
                    .Find(filter)
                    .Sort(sort)
                    .Limit(_batchSize)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                if (batch.Count == 0)
                    break;

                foreach (var request in batch)
                {
                    lastSeenId = request["_id"];
                    await _catchUpChannel.Writer.WriteAsync(request, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Catch-up cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Catch-up failed");
            _catchUpChannel.Writer.TryComplete(ex);
            return;
        }

        _catchUpChannel.Writer.TryComplete();
    }

    private async Task PumpAsync(ChannelWriter<BsonDocument> output, CancellationToken ct)
    {
        try
        {
            // Phase 1: drain catch-up fully before processing any live items.
            await foreach (var item in _catchUpChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                await output.WriteAsync(item, ct).ConfigureAwait(false);

            _logger.LogInformation("Switching to live mode");

            // Phase 2: drain live indefinitely until canceled or channel completed.
            await foreach (var item in _liveChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                await output.WriteAsync(item, ct).ConfigureAwait(false);

            output.TryComplete();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            output.TryComplete();
        }
        catch (Exception ex)
        {
            output.TryComplete(ex);
        }
    }
}