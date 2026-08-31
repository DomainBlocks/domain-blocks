using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB;

public record AppendContext(Guid CommitId, string StreamId, ExpectedStreamState<StreamPosition> ExpectedStreamState);