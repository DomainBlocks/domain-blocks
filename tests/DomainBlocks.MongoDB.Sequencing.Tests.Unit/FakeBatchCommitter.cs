using MongoDB.Bson;

namespace DomainBlocks.MongoDB.Sequencing.Tests.Unit;

/// <summary>
/// Represents a committer whose target collection has a unique index on each document's value, so a commit conflicts on
/// a value that's already committed or repeated in the batch. A conflict's operation time is its attempt's number,
/// counting from 1.
/// </summary>
internal sealed class FakeBatchCommitter(params string[] committedValues) : IBatchCommitter
{
    public const string ValueFieldName = "value";

    private readonly List<string> _committedValues = [.. committedValues];
    private readonly List<string> _commitAttempts = [];
    private readonly Queue<Action?> _abortedAttempts = [];

    /// <summary>
    /// Gets the values committed, in order.
    /// </summary>
    public IReadOnlyList<string> CommittedValues => _committedValues;

    /// <summary>
    /// Gets the values of the documents in each commit attempt, joined with commas, in the order of the attempts.
    /// </summary>
    public IReadOnlyList<string> CommitAttempts => _commitAttempts;

    public static BsonDocument CreateDocument(string value) => new(ValueFieldName, value);

    /// <summary>
    /// Makes the next commit attempt fail with a transient error, as a write conflict with another commit would, after
    /// running the given action during the attempt.
    /// </summary>
    public void AbortNextAttempt(Action? duringAttempt = null) => _abortedAttempts.Enqueue(duringAttempt);

    public Task<CommitResult> CommitAsync(IReadOnlyList<BsonDocument> documents, CancellationToken cancellationToken)
    {
        string[] values = [.. documents.Select(x => x[ValueFieldName].AsString)];
        _commitAttempts.Add(string.Join(", ", values));

        if (_abortedAttempts.TryDequeue(out var duringAttempt))
        {
            duringAttempt?.Invoke();
            return Task.FromResult<CommitResult>(new CommitResult.TransientError());
        }

        for (var i = 0; i < values.Length; i++)
        {
            if (!_committedValues.Contains(values[i]) && Array.IndexOf(values, values[i]) == i)
                continue;

            var message = $"Duplicate value '{values[i]}'.";
            var exception = new InvalidOperationException(message);
            var operationTime = new BsonTimestamp(_commitAttempts.Count);

            return Task.FromResult<CommitResult>(new CommitResult.Conflict(i, message, exception, operationTime));
        }

        _committedValues.AddRange(values);
        return Task.FromResult<CommitResult>(new CommitResult.Success());
    }
}