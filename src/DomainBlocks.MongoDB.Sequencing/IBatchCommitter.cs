using MongoDB.Bson;

namespace DomainBlocks.MongoDB.Sequencing;

/// <summary>
/// Commits a batch's documents with the next numbers of a sequence.
/// </summary>
internal interface IBatchCommitter
{
    /// <summary>
    /// Writes the next sequence numbers into the documents, in order, and commits the documents together, or none of
    /// them. Requires at least one document.
    /// </summary>
    Task<CommitResult> CommitAsync(IReadOnlyList<BsonDocument> documents, CancellationToken cancellationToken);
}