using System.Threading.Channels;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public sealed class SequenceBinding<TDocument>(
    string sequenceCollectionFullName,
    string targetCollectionFullName,
    string sequenceId,
    FieldDefinition<TDocument, long> targetField)
{
    public string SequenceCollectionFullName { get; } = sequenceCollectionFullName;
    public string TargetCollectionFullName { get; } = targetCollectionFullName;
    public string SequenceId { get; } = sequenceId;
    public FieldDefinition<TDocument, long> TargetField { get; } = targetField;
}

public class SequencedAppenderOptions
{
    public int QueueCapacity { get; set; } = 1_000;
    public int BatchSize { get; set; } = 500;
}

public class AppendOptions
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}

public sealed class PendingAppend<TContext>
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PendingAppend(IReadOnlyList<BsonDocument> documents, TContext context)
    {
        if (documents.Count == 0)
            throw new ArgumentException("At least one document is required.", nameof(documents));

        Documents = documents;
        Context = context;
    }

    public IReadOnlyList<BsonDocument> Documents { get; }
    public TContext Context { get; }
    public bool IsCompleted => _tcs.Task.IsCompleted;
    internal Task Completion => _tcs.Task;

    public bool TryComplete(Exception? error = null)
    {
        return error is null ? _tcs.TrySetResult() : _tcs.TrySetException(error);
    }

    public bool TryFail(Exception exception) => _tcs.TrySetException(exception);
}

public interface ISequencedAppendPolicy<TDocument, TContext>
{
    ValueTask OnCommittingAsync(IReadOnlyList<PendingAppend<TContext>> batch, CancellationToken cancellationToken);

    void OnConflict(PendingAppend<TContext> conflictingAppend);
}

public sealed class NullSequencedAppendPolicy<TDocument, TContext> : ISequencedAppendPolicy<TDocument, TContext>
{
    public static readonly NullSequencedAppendPolicy<TDocument, TContext> Instance = new();

    public ValueTask OnCommittingAsync(
        IReadOnlyList<PendingAppend<TContext>> batch,
        CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public void OnConflict(PendingAppend<TContext> conflictingAppend)
    {
    }
}

public sealed class AppendToStreamPolicy(IMongoCollection<BsonDocument> eventLog) :
    ISequencedAppendPolicy<BsonDocument, AppendToStreamContext>
{
    private readonly PreAppendQuery _preAppendQuery = new(eventLog);
    private readonly Buffers _buffers = new();

    public async ValueTask OnCommittingAsync(
        IReadOnlyList<PendingAppend<AppendToStreamContext>> batch,
        CancellationToken cancellationToken)
    {
        _buffers.ClearAll();
        _preAppendQuery.Reset();

        foreach (var append in batch)
        {
            var bsonCommitId = append.Documents[0][EventLogEntry.FieldNames.CommitId];
            var bsonStreamId = append.Documents[0][EventLogEntry.FieldNames.StreamId];
            _preAppendQuery.AddInput(bsonCommitId, bsonStreamId);
        }

        await _preAppendQuery
            .ExecuteAsync(
                _buffers.ExistingCommitIds,
                _buffers.HeadStreamVersions,
                cancellationToken)
            .ConfigureAwait(false);

        var writtenAtUtc = DateTime.UtcNow;

        foreach (var append in batch)
        {
            var commitId = append.Context.CommitId;

            if (!_buffers.SeenCommitIds.Add(commitId))
                continue;

            if (_buffers.ExistingCommitIds.Contains(commitId))
            {
                append.TryComplete();
                continue;
            }

            var streamId = append.Context.StreamId;
            var expectedState = append.Context.ExpectedState;
            var streamVersion = _buffers.HeadStreamVersions.GetValueOrDefault(streamId, -1L);

            var actualState = streamVersion < 0
                ? StreamState.StreamDoesNotExist
                : StreamState.StreamExists(StreamVersion.FromInt64(streamVersion));

            if (!expectedState.Matches(actualState))
            {
                append.TryComplete(new StreamAppendConflictException(streamId, expectedState, actualState));
                continue;
            }

            foreach (var e in append.Documents)
            {
                e[EventLogEntry.FieldNames.StreamVersion] = ++streamVersion;
                e[EventLogEntry.FieldNames.WrittenAtUtc] = writtenAtUtc;
            }

            _buffers.HeadStreamVersions[streamId] = streamVersion;
        }
    }

    public void OnConflict(PendingAppend<AppendToStreamContext> conflictingAppend)
    {
        var isPermanentConflict = conflictingAppend.Context.ExpectedState.IsStreamDoesNotExist ||
                                  conflictingAppend.Context.ExpectedState.IsSpecificVersion;

        if (!isPermanentConflict)
            return;

        var exception = new StreamAppendConflictException(
            conflictingAppend.Context.StreamId,
            conflictingAppend.Context.ExpectedState);

        conflictingAppend.TryComplete(exception);
    }

    private sealed class Buffers
    {
        public readonly HashSet<Guid> SeenCommitIds = [];
        public readonly HashSet<Guid> ExistingCommitIds = [];
        public readonly Dictionary<string, long> HeadStreamVersions = [];

        public void ClearAll()
        {
            SeenCommitIds.Clear();
            ExistingCommitIds.Clear();
            HeadStreamVersions.Clear();
        }
    }
}

public record AppendToStreamContext(Guid CommitId, string StreamId, ExpectedStreamState ExpectedState);

public class SequencedAppender<TDocument, TContext>
{
    private readonly IMongoClient _mongoClient;
    private readonly IMongoCollection<BsonDocument> _sequenceCollection;
    private readonly IMongoCollection<BsonDocument> _targetCollection;
    private readonly string _sequenceId;
    private readonly string[] _targetFieldPathSegments;
    private readonly ISequencedAppendPolicy<TDocument, TContext> _appendPolicy;
    private readonly ILogger _logger;
    private readonly Channel<PendingAppend<TContext>> _channel;
    private readonly int _batchSize;
    private readonly CancellationTokenSource _stopCts = new();
    private readonly Task _runAppendLoopTask;
    private readonly Buffers _buffers = new();
    private int _disposed;

    public SequencedAppender(
        IMongoClient mongoClient,
        SequenceBinding<TDocument> binding,
        ISequencedAppendPolicy<TDocument, TContext>? appendPolicy = null,
        SequencedAppenderOptions? options = null,
        ILogger? logger = null)
    {
        var sequenceCollectionNs = CollectionNamespace.FromFullName(binding.SequenceCollectionFullName);
        var targetCollectionNs = CollectionNamespace.FromFullName(binding.TargetCollectionFullName);

        var serializerRegistry = BsonSerializer.SerializerRegistry;
        var documentSerializer = serializerRegistry.GetSerializer<TDocument>();

        var renderArgs = new RenderArgs<TDocument>(
            documentSerializer,
            serializerRegistry,
            translationOptions: mongoClient.Settings.TranslationOptions);

        var targetFieldName = binding.TargetField.Render(renderArgs).FieldName;

        options ??= new SequencedAppenderOptions();

        _mongoClient = mongoClient;

        _sequenceCollection = mongoClient
            .GetDatabase(sequenceCollectionNs.DatabaseNamespace.DatabaseName)
            .GetCollection<BsonDocument>(sequenceCollectionNs.CollectionName);

        _targetCollection = mongoClient
            .GetDatabase(targetCollectionNs.DatabaseNamespace.DatabaseName)
            .GetCollection<BsonDocument>(targetCollectionNs.CollectionName);

        _sequenceId = binding.SequenceId;
        _targetFieldPathSegments = targetFieldName.Split('.');
        _appendPolicy = appendPolicy ?? NullSequencedAppendPolicy<TDocument, TContext>.Instance;
        _logger = logger ?? NullLogger.Instance;

        _channel = Channel.CreateBounded<PendingAppend<TContext>>(new BoundedChannelOptions(options.QueueCapacity)
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
        var documentArray = documents.Select(x => x.ToBsonDocument()).ToArray();
        if (documentArray.Length == 0)
            return;

        options ??= new AppendOptions();
        var pendingAppend = new PendingAppend<TContext>(documentArray, context);

        using var linkedTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedTimeoutCts.CancelAfter(options.Timeout);

        try
        {
            await _channel.Writer.WriteAsync(pendingAppend, linkedTimeoutCts.Token);
            await pendingAppend.Completion.WaitAsync(linkedTimeoutCts.Token).ConfigureAwait(false);
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
        var batch = new List<PendingAppend<TContext>>(_batchSize);

        try
        {
            while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                batch.Clear();

                while (batch.Count < _batchSize && _channel.Reader.TryRead(out var append))
                    batch.Add(append);

                if (batch.Count == 0)
                    continue;

                //await ProcessBatchAsync(batch, ct).ConfigureAwait(false);
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

    private async Task ProcessAppendBatchAsync(List<PendingAppend<TContext>> batch, CancellationToken ct)
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
                        _appendPolicy.OnConflict(conflict.ConflictingAppend);

                        if (conflict.ConflictingAppend.IsCompleted)
                            batch.Remove(conflict.ConflictingAppend);
                        break;
                    }
                }
            }
            catch (MongoException ex) when (ex.HasErrorLabel(MongoErrorLabels.TransientTransactionError))
            {
                // Transient error - retry the whole pending batch.
                _logger.LogTrace("Transient transaction error; retrying");
            }
        }
    }

    private async Task PrepareCommitAsync(List<PendingAppend<TContext>> batch, CancellationToken ct)
    {
        _buffers.ClearAll();

        await _appendPolicy.OnCommittingAsync(batch, ct);

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
        session.StartTransaction(MongoEventStoreClient2.TransactionOptions);

        try
        {
            var docs = _buffers.OutgoingDocuments;
            var startSeq = await ClaimSequenceAsync(session, docCount, ct).ConfigureAwait(false);

            for (var i = 0; i < docCount; i++)
                SetSequenceValue(docs[i], _targetFieldPathSegments, startSeq + i);

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

    private static void SetSequenceValue(BsonDocument doc, ReadOnlySpan<string> pathSegments, long value)
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

    private static void FaultAll(List<PendingAppend<TContext>> appends, Exception exception)
    {
        foreach (var append in appends)
            append.TryComplete(exception);
    }

    private static void DrainWithFault(ChannelReader<PendingAppend<TContext>> reader, Exception exception)
    {
        while (reader.TryRead(out var pending))
            pending.TryComplete(exception);
    }

    private sealed class Buffers
    {
        public readonly List<PendingAppend<TContext>> OutgoingAppends = [];
        public readonly List<BsonDocument> OutgoingDocuments = [];
        public readonly List<PendingAppend<TContext>> AppendIndexMap = [];

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

        public sealed class Conflict(PendingAppend<TContext> conflictingAppend) : CommitResult
        {
            public PendingAppend<TContext> ConflictingAppend { get; } = conflictingAppend;
        }
    }
}