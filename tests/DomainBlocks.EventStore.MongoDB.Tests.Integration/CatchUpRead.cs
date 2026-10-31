namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

/// <summary>
/// Represents a read of the events that a catch-up sent to the server.
/// </summary>
internal sealed record CatchUpRead(long StartExclusive, long EndInclusive);