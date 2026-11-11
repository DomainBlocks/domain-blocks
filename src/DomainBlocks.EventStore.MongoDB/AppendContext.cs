namespace DomainBlocks.EventStore.MongoDB;

public record AppendContext(
    Guid CommitId,
    bool IsCommitIdGenerated,
    string StreamId,
    ExpectedStreamState<StreamPosition> ExpectedStreamState);