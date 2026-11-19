using MongoDB.Bson;

namespace DomainBlocks.MongoDB.Sequencing.Tests.Unit;

/// <summary>
/// Represents a committer whose target collection has a unique index on each document's value, so a commit conflicts on
/// a value that's already committed or repeated in the batch.
/// </summary>
internal sealed class FakeBatchCommitter(params string[] committedValues) : IBatchCommitter
{
    public const string ValueFieldName = "value";

    private readonly List<string> _committedValues = [.. committedValues];
    private readonly List<string> _commitAttempts = [];

    /// <summary>
    /// Gets the values committed, in order.
    /// </summary>
    public IReadOnlyList<string> CommittedValues => _committedValues;

    /// <summary>
    /// Gets the values of the documents in each commit attempt, joined with commas, in the order of the attempts.
    /// </summary>
    public IReadOnlyList<string> CommitAttempts => _commitAttempts;

    public static BsonDocument CreateDocument(string value) => new(ValueFieldName, value);

    public Task<CommitResult> CommitAsync(IReadOnlyList<BsonDocument> documents, CancellationToken cancellationToken)
    {
        string[] values = [.. documents.Select(x => x[ValueFieldName].AsString)];
        _commitAttempts.Add(string.Join(", ", values));

        for (var i = 0; i < values.Length; i++)
        {
            if (!_committedValues.Contains(values[i]) && Array.IndexOf(values, values[i]) == i)
                continue;

            var message = $"Duplicate value '{values[i]}'.";
            var exception = new InvalidOperationException(message);

            return Task.FromResult<CommitResult>(new CommitResult.Conflict(i, message, exception));
        }

        _committedValues.AddRange(values);
        return Task.FromResult<CommitResult>(new CommitResult.Success());
    }
}