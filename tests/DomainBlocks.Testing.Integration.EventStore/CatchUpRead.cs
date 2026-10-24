namespace DomainBlocks.Testing.Integration.EventStore;

public sealed record CatchUpRead(long StartExclusive, long EndInclusive, bool IsCompleted);