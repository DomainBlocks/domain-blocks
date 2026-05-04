using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB;

public record AppendToStreamContext(Guid CommitId, string StreamId, ExpectedStreamState ExpectedState);