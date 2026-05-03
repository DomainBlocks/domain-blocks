using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace DomainBlocks.MongoDB.Sequencing;

internal static class MongoSequencedAppender
{
    public static readonly TransactionOptions TransactionOptions = new(
        ReadConcern.Snapshot,
        ReadPreference.Primary,
        WriteConcern.WMajority.With(journal: true));
}

public class MongoSequencedAppender<TDocument, TContext> : IMongoSequencedAppender<TDocument, TContext>
{
    private readonly IMongoClient _mongoClient;
    private readonly IMongoCollection<BsonDocument> _sequenceCollection;
    private readonly IMongoCollection<BsonDocument> _targetCollection;
    private readonly string _sequenceId;
    private readonly string[] _targetFieldPathSegments;
    private readonly IMongoSequencedAppenderPolicy<TContext> _appenderPolicy;
    private readonly ILogger _logger;
    private readonly Channel<AppendEntry<TContext>> _channel;
    private readonly int _batchSize;
    private readonly CancellationTokenSource _stopCts = new();
    private readonly Task _runAppendLoopTask;
    private readonly Buffers _buffers = new();
    private int _disposed;

    public MongoSequencedAppender(
        IMongoClient mongoClient,
        MongoSequenceBinding<TDocument> binding,
        IMongoSequencedAppenderPolicy<TContext>? appendPolicy = null,
        MongoSequencedAppenderOptions? options = null,
        ILogger? logger = null)
    {
        var serializerRegistry = BsonSerializer.SerializerRegistry;
        var documentSerializer = serializerRegistry.GetSerializer<TDocument>();

        var renderArgs = new RenderArgs<TDocument>(
            documentSerializer,
            serializerRegistry,
            translationOptions: mongoClient.Settings.TranslationOptions);

        var targetFieldName = binding.TargetField.Render(renderArgs).FieldName;

        options ??= new MongoSequencedAppenderOptions();

        _mongoClient = mongoClient;

        _sequenceCollection = mongoClient
            .GetDatabase(binding.SequenceCollectionNamespace.DatabaseNamespace.DatabaseName)
            .GetCollection<BsonDocument>(binding.SequenceCollectionNamespace.CollectionName);

        _targetCollection = mongoClient
            .GetDatabase(binding.TargetCollectionNamespace.DatabaseNamespace.DatabaseName)
            .GetCollection<BsonDocument>(binding.TargetCollectionNamespace.CollectionName);

        _sequenceId = binding.SequenceId;
        _targetFieldPathSegments = targetFieldName.Split('.');
        _appenderPolicy = appendPolicy ?? NullSequencedAppenderPolicy<TDocument, TContext>.Instance;
        _logger = logger ?? NullLogger.Instance;

        _channel = Channel.CreateBounded<AppendEntry<TContext>>(new BoundedChannelOptions(options.QueueCapacity)
        {
            SingleWriter = false,
            SingleReader = true
        });

        _batchSize = options.BatchSize;
        _runAppendLoopTask = RunAppendLoopAsync(_stopCts.Token);
    }

    public async Task AppendAsync(
        IEnumerable<TDocument> documents,
        TContext context,
        AppendOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var bsonDocuments = documents.Select(x => x.ToBsonDocument()).ToArray();
        if (bsonDocuments.Length == 0)
            return;

        options ??= new AppendOptions();
        var appendItem = new AppendEntry<TContext>(bsonDocuments, context);

        using var linkedTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedTimeoutCts.CancelAfter(options.Timeout);

        try
        {
            await _channel.Writer.WriteAsync(appendItem, linkedTimeoutCts.Token);
            await appendItem.Completion.WaitAsync(linkedTimeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller canceled.
            throw;
        }
        catch (OperationCanceledException) when (linkedTimeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"Append operation did not complete within {options.Timeout}.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _channel.Writer.TryComplete();
        await _stopCts.CancelAsync().ConfigureAwait(false);
        await _runAppendLoopTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _stopCts.Dispose();
    }

    private async Task RunAppendLoopAsync(CancellationToken ct)
    {
        var batch = new List<AppendEntry<TContext>>(_batchSize);

        try
        {
            while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (batch.Count < _batchSize && _channel.Reader.TryRead(out var append))
                    batch.Add(append);

                if (batch.Count == 0)
                    continue;

                await ProcessBatchAsync(batch, ct).ConfigureAwait(false);
                batch.Clear();
            }
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            // Graceful stop: complete writer, then fault current batch + queued items.
            _channel.Writer.TryComplete();
            FaultAll(batch, ex);
            DrainWithFault(_channel, ex);
        }
        catch (Exception ex)
        {
            // Fatal worker failure: complete writer with error, then current batch + queued items.
            _logger.LogCritical(ex, "A fatal error occurred while processing appends");
            _channel.Writer.TryComplete(ex);
            FaultAll(batch, ex);
            DrainWithFault(_channel, ex);
        }
    }

    private async Task ProcessBatchAsync(List<AppendEntry<TContext>> batch, CancellationToken ct)
    {
        while (batch.Count > 0)
        {
            try
            {
                await PrepareCommitAsync(batch, ct).ConfigureAwait(false);
                var result = await CommitAsync(ct).ConfigureAwait(false);

                switch (result)
                {
                    case CommitResult.Success:
                        return;
                    case CommitResult.Conflict conflict:
                    {
                        _appenderPolicy.OnConflict(conflict.ConflictingAppend);

                        if (conflict.ConflictingAppend.IsCompleted)
                            batch.Remove(conflict.ConflictingAppend);

                        break;
                    }
                }
            }
            catch (MongoException ex) when (ex.HasErrorLabel(MongoErrorLabels.TransientTransactionError))
            {
                // Transient error - retry the whole batch.
                _logger.LogTrace("Transient transaction error; retrying");
            }
        }
    }

    private async Task PrepareCommitAsync(List<AppendEntry<TContext>> batch, CancellationToken ct)
    {
        _buffers.ClearAll();

        await _appenderPolicy.OnCommittingAsync(batch, ct);

        foreach (var append in batch)
        {
            if (append.IsCompleted)
                continue;

            _buffers.OutgoingAppends.Add(append);

            foreach (var document in append.Documents)
            {
                _buffers.OutgoingDocuments.Add(document);
                _buffers.AppendIndexMap.Add(append);
            }
        }
    }

    private async Task<CommitResult> CommitAsync(CancellationToken ct)
    {
        var docCount = _buffers.OutgoingDocuments.Count;
        if (docCount == 0)
            return new CommitResult.Success();

        using var session = await _mongoClient.StartSessionAsync(cancellationToken: ct).ConfigureAwait(false);
        session.StartTransaction(MongoSequencedAppender.TransactionOptions);

        try
        {
            var docs = _buffers.OutgoingDocuments;
            var startSeq = await ClaimSequenceAsync(session, docCount, ct).ConfigureAwait(false);

            for (var i = 0; i < docCount; i++)
                SetSequenceField(docs[i], _targetFieldPathSegments, startSeq + i);

            await _targetCollection
                .InsertManyAsync(session, docs, new InsertManyOptions { IsOrdered = true }, ct)
                .ConfigureAwait(false);

            await session.CommitWithRetryOnUnknownResultAsync(_logger, ct).ConfigureAwait(false);
        }
        catch (MongoBulkWriteException ex) when (ex.WriteErrors.Any(x => x.Code == MongoErrorCodes.DuplicateKey))
        {
            await SafeAbortTransactionAsync(session, ct).ConfigureAwait(false);

            var firstError = ex.WriteErrors.First(e => e.Code == MongoErrorCodes.DuplicateKey);
            var conflictingDoc = _buffers.OutgoingDocuments[firstError.Index];
            var conflictingAppend = _buffers.AppendIndexMap[firstError.Index];
            return new CommitResult.Conflict(conflictingAppend);
        }
        catch (Exception)
        {
            await SafeAbortTransactionAsync(session, ct).ConfigureAwait(false);
            throw;
        }

        foreach (var append in _buffers.OutgoingAppends)
            append.TryComplete();

        return new CommitResult.Success();
    }

    private async Task<long> ClaimSequenceAsync(IClientSessionHandle session, long count, CancellationToken ct)
    {
        const string nextFieldName = "next";

        var filter = Builders<BsonDocument>.Filter.Eq("_id", _sequenceId);
        var update = Builders<BsonDocument>.Update.Inc(nextFieldName, count);

        var options = new FindOneAndUpdateOptions<BsonDocument>
        {
            IsUpsert = true,
            Projection = Builders<BsonDocument>.Projection.Include(nextFieldName),
            ReturnDocument = ReturnDocument.Before
        };

        var result = await _sequenceCollection
            .FindOneAndUpdateAsync(session, filter, update, options, ct)
            .ConfigureAwait(false);

        var start = result?[nextFieldName].ToInt64() ?? 0;

        return start;
    }

    private static void SetSequenceField(BsonDocument doc, ReadOnlySpan<string> pathSegments, long value)
    {
        var currentDoc = doc;

        for (var i = 0; i < pathSegments.Length - 1; i++)
        {
            var segment = pathSegments[i];

            if (!currentDoc.TryGetValue(segment, out var child))
            {
                var childDoc = new BsonDocument();
                currentDoc[segment] = childDoc;
                currentDoc = childDoc;
                continue;
            }

            {
                if (child is not BsonDocument childDoc)
                {
                    var fullPath = string.Join('.', pathSegments);
                    throw new InvalidOperationException(
                        $"Path '{fullPath}' invalid at '{segment}': expected BsonDocument, got {child.BsonType}.");
                }

                currentDoc = childDoc;
            }
        }

        currentDoc[pathSegments[^1]] = value;
    }

    private async Task SafeAbortTransactionAsync(IClientSessionHandle session, CancellationToken ct)
    {
        if (!session.IsInTransaction)
            return;

        try
        {
            await session.AbortTransactionAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogTrace("Transaction canceled");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to abort transaction");
        }
    }

    private static void FaultAll(List<AppendEntry<TContext>> appends, Exception exception)
    {
        foreach (var append in appends)
            append.TryComplete(exception);
    }

    private static void DrainWithFault(ChannelReader<AppendEntry<TContext>> reader, Exception exception)
    {
        while (reader.TryRead(out var append))
            append.TryComplete(exception);
    }

    private sealed class Buffers
    {
        public readonly List<AppendEntry<TContext>> OutgoingAppends = [];
        public readonly List<BsonDocument> OutgoingDocuments = [];
        public readonly List<AppendEntry<TContext>> AppendIndexMap = [];

        public void ClearAll()
        {
            OutgoingAppends.Clear();
            OutgoingDocuments.Clear();
            AppendIndexMap.Clear();
        }
    }

    private abstract class CommitResult
    {
        public sealed class Success : CommitResult;

        public sealed class Conflict(AppendEntry<TContext> conflictingAppend) : CommitResult
        {
            public AppendEntry<TContext> ConflictingAppend { get; } = conflictingAppend;
        }
    }
}