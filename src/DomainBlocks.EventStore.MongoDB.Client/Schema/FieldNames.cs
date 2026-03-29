namespace DomainBlocks.EventStore.MongoDB.Client.Schema;

public static class FieldNames
{
    public const string ActualStreamState = "actualStreamState";
    public const string AppendedCommitIds = "appendedCommitIds";
    public const string CommitId = "commitId";
    public const string CommitPosition = "commitPosition";
    public const string CreatedAtUtc = "createdAtUtc";
    public const string DuplicateCommitIds = "duplicateCommitIds";
    public const string Epoch = "epoch";
    public const string EventData = "eventData";
    public const string EventName = "eventName";
    public const string Events = "events";
    public const string ExpectedStreamState = "expectedStreamState";
    public const string Metadata = "metadata";
    public const string Rejections = "rejections";
    public const string StreamId = "streamId";
    public const string StreamVersion = "streamVersion";
    public const string WrittenAtUtc = "writtenAtUtc";
}