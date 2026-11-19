namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

/// <summary>
/// Represents one of the appends that a <see cref="BatchingScenario"/> makes in one batch.
/// </summary>
internal sealed record BatchedAppend(
    string StreamId,
    ExpectedStreamState<StreamPosition> ExpectedState,
    Guid? CommitId);