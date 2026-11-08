using MongoDB.Bson;

namespace DomainBlocks.MongoDB.Sequencing;

/// <summary>
/// Represents an append request, containing the BSON documents to append and caller-supplied context.
/// </summary>
public sealed class AppendRequest<TContext>
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private AppendRequest<TContext>? _nextAttached;

    public AppendRequest(IReadOnlyList<BsonDocument> documents, TContext context)
        : this(documents, context, Guid.NewGuid())
    {
    }

    internal AppendRequest(IReadOnlyList<BsonDocument> documents, TContext context, Guid id)
    {
        ArgumentNullException.ThrowIfNull(documents);

        if (documents.Count == 0)
            throw new ArgumentException("At least one document is required.", nameof(documents));

        Documents = documents;
        Context = context;
        Id = id;
    }

    /// <summary>
    /// Gets the BSON documents to be appended. Always contains at least one document.
    /// </summary>
    public IReadOnlyList<BsonDocument> Documents { get; }

    /// <summary>
    /// Gets the caller-supplied context associated with the append operation.
    /// </summary>
    public TContext Context { get; }

    /// <summary>
    /// Gets a value that indicates whether this request has completed.
    /// </summary>
    public bool IsCompleted => _tcs.Task.IsCompleted;

    internal Guid Id { get; }
    internal Task Completion => _tcs.Task;

    /// <summary>
    /// Attempts to complete this request successfully, or with a failure if an exception is provided.
    /// </summary>
    /// <returns>True if the request has completed, or false if it has already been completed.</returns>
    public bool TryComplete(Exception? error = null)
    {
        if (!TrySetOutcome(error))
            return false;

        for (var request = _nextAttached; request is not null; request = request._nextAttached)
            request.TrySetOutcome(error);

        return true;
    }

    /// <summary>
    /// Makes another request with this request's ID complete with this request's outcome. Called before the policy sees
    /// either request, so neither can have been completed yet.
    /// </summary>
    internal void Attach(AppendRequest<TContext> request)
    {
        request._nextAttached = _nextAttached;
        _nextAttached = request;
    }

    private bool TrySetOutcome(Exception? error)
    {
        return error is null ? _tcs.TrySetResult() : _tcs.TrySetException(error);
    }
}