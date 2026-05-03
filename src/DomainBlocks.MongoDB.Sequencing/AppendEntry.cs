using MongoDB.Bson;

namespace DomainBlocks.MongoDB.Sequencing;

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

    public IReadOnlyList<BsonDocument> Documents { get; }
    public TContext Context { get; }
    public bool IsCompleted => _tcs.Task.IsCompleted;
    internal Task Completion => _tcs.Task;

    public bool TryComplete(Exception? error = null)
    {
        return error is null ? _tcs.TrySetResult() : _tcs.TrySetException(error);
    }
}