namespace DomainBlocks.MongoDB.Sequencing;

/// <summary>
/// Appends documents to a MongoDB collection with a globally ordered sequence number, providing a total ordering key
/// across change streams and historical reads.
/// </summary>
public interface IMongoSequencedAppender<in TDocument, in TContext> : IAsyncDisposable
{
    /// <summary>
    /// Appends one or more documents, assigning each a contiguous globally ordered sequence number.
    /// </summary>
    /// <param name="documents">The documents to append.</param>
    /// <param name="context">The caller-supplied context to associate with this append operation.</param>
    /// <param name="requestId">
    /// The ID of this append request, or <see langword="null"/> to generate one. Requests with the same ID in one batch
    /// are treated as one request: the policy receives only the first, only its documents are appended, and all of them
    /// complete with its outcome.
    /// </param>
    /// <param name="options">Optional per-call options.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task AppendAsync(
        IEnumerable<TDocument> documents,
        TContext context,
        Guid? requestId = null,
        AppendOptions? options = null,
        CancellationToken cancellationToken = default);
}