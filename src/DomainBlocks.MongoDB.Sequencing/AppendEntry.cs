using MongoDB.Bson;

namespace DomainBlocks.MongoDB.Sequencing;

/// <summary>
/// Represents the documents and context associated with a single append operation.
/// </summary>
public sealed class AppendEntry<TContext>
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AppendEntry(IReadOnlyList<BsonDocument> documents, TContext context)
    {
        if (documents.Count == 0)
            throw new ArgumentException("At least one document is required.", nameof(documents));

        Documents = documents;
        Context = context;
    }

    /// <summary>
    /// The BSON documents to be appended.
    /// </summary>
    public IReadOnlyList<BsonDocument> Documents { get; }

    /// <summary>
    /// The caller-supplied context associated with this append.
    /// </summary>
    public TContext Context { get; }

    internal Guid Id { get; } = Guid.NewGuid();
    internal bool IsCompleted => _tcs.Task.IsCompleted;
    internal Task Completion => _tcs.Task;

    internal bool TryComplete(Exception? error = null)
    {
        return error is null ? _tcs.TrySetResult() : _tcs.TrySetException(error);
    }
}