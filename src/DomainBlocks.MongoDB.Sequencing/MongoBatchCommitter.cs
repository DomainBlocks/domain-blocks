using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace DomainBlocks.MongoDB.Sequencing;

internal static class MongoBatchCommitter
{
    public static readonly TransactionOptions TransactionOptions = new(
        ReadConcern.Snapshot,
        ReadPreference.Primary,
        WriteConcern.WMajority.With(journal: true));

    public static readonly InsertManyOptions InsertManyOptions = new() { IsOrdered = true };

    public static readonly TimeSpan CommitRetryDelay = TimeSpan.FromMilliseconds(100);
}

/// <summary>
/// Commits a batch's documents in a MongoDB transaction that also claims their sequence numbers from the sequence
/// document.
/// </summary>
internal sealed class MongoBatchCommitter<TDocument> : IBatchCommitter
{
    private const int MaxCommitRetries = 3;

    private readonly IMongoClient _mongoClient;
    private readonly IMongoCollection<BsonDocument> _sequenceCollection;
    private readonly IMongoCollection<BsonDocument> _targetCollection;
    private readonly string _sequenceId;
    private readonly string[] _targetFieldPathSegments;
    private readonly ILogger _logger;

    public MongoBatchCommitter(IMongoClient mongoClient, MongoSequenceBinding<TDocument> binding, ILogger logger)
    {
        var serializerRegistry = BsonSerializer.SerializerRegistry;
        var documentSerializer = serializerRegistry.GetSerializer<TDocument>();

        var renderArgs = new RenderArgs<TDocument>(
            documentSerializer,
            serializerRegistry,
            translationOptions: mongoClient.Settings.TranslationOptions);

        var targetFieldName = binding.TargetField.Render(renderArgs).FieldName;

        _mongoClient = mongoClient;

        _sequenceCollection = mongoClient
            .GetDatabase(binding.SequenceCollectionNamespace.DatabaseNamespace.DatabaseName)
            .GetCollection<BsonDocument>(binding.SequenceCollectionNamespace.CollectionName);

        _targetCollection = mongoClient
            .GetDatabase(binding.TargetCollectionNamespace.DatabaseNamespace.DatabaseName)
            .GetCollection<BsonDocument>(binding.TargetCollectionNamespace.CollectionName);

        _sequenceId = binding.SequenceId;
        _targetFieldPathSegments = targetFieldName.Split('.');
        _logger = logger;
    }

    public async Task<CommitResult> CommitAsync(
        IReadOnlyList<BsonDocument> documents,
        CancellationToken cancellationToken)
    {
        var docCount = documents.Count;

        while (true)
        {
            using var session = await _mongoClient
                .StartSessionAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            session.StartTransaction(MongoBatchCommitter.TransactionOptions);

            try
            {
                var startSeq = await ClaimSequenceAsync(session, docCount, cancellationToken).ConfigureAwait(false);

                for (var i = 0; i < docCount; i++)
                    SetSequenceField(documents[i], _targetFieldPathSegments, startSeq + i);

                await _targetCollection
                    .InsertManyAsync(session, documents, MongoBatchCommitter.InsertManyOptions, cancellationToken)
                    .ConfigureAwait(false);

                await CommitWithRetryAsync(session, cancellationToken).ConfigureAwait(false);
                return new CommitResult.Success();
            }
            catch (MongoException ex) when (ex.HasErrorLabel(MongoErrorLabels.TransientTransactionError))
            {
                await SafeAbortTransactionAsync(session, cancellationToken).ConfigureAwait(false);
                _logger.LogTrace("Transient transaction error; retrying transaction (size={BatchSize})", docCount);
            }
            catch (MongoBulkWriteException ex) when (ex.WriteErrors.Any(x => x.Code == MongoErrorCodes.DuplicateKey))
            {
                await SafeAbortTransactionAsync(session, cancellationToken).ConfigureAwait(false);

                var firstError = ex.WriteErrors.First(e => e.Code == MongoErrorCodes.DuplicateKey);
                return new CommitResult.Conflict(firstError.Index, firstError.Message, ex);
            }
            catch (Exception)
            {
                await SafeAbortTransactionAsync(session, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
    }

    private async Task<long> ClaimSequenceAsync(IClientSessionHandle session, long count, CancellationToken ct)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("_id", _sequenceId);
        var update = Builders<BsonDocument>.Update.Inc(SequenceFieldNames.Next, count);

        var options = new FindOneAndUpdateOptions<BsonDocument>
        {
            IsUpsert = true,
            Projection = Builders<BsonDocument>.Projection.Include(SequenceFieldNames.Next),
            ReturnDocument = ReturnDocument.Before
        };

        var result = await _sequenceCollection
            .FindOneAndUpdateAsync(session, filter, update, options, ct)
            .ConfigureAwait(false);

        var start = result?[SequenceFieldNames.Next].ToInt64() ?? 0;

        return start;
    }

    private static void SetSequenceField(BsonDocument doc, ReadOnlySpan<string> pathSegments, long value)
    {
        var currentDoc = doc;

        for (var i = 0; i < pathSegments.Length - 1; i++)
        {
            var segment = pathSegments[i];

            if (!currentDoc.TryGetValue(segment, out var child) || child.IsBsonNull)
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

    private async Task CommitWithRetryAsync(IClientSessionHandle session, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxCommitRetries; attempt++)
        {
            try
            {
                await session.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (MongoException ex) when (ex.HasErrorLabel(MongoErrorLabels.UnknownTransactionCommitResult))
            {
                _logger.LogWarning(ex, "Unknown transaction commit result; retrying");
                await Task.Delay(MongoBatchCommitter.CommitRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        // Final attempt - let it throw
        await session.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
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
}