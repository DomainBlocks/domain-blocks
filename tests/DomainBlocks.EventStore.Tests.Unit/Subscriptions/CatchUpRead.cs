namespace DomainBlocks.EventStore.Tests.Unit.Subscriptions;

/// <summary>
/// A read that a catch-up made, from after one position, or from the start if it is <see langword="null"/>, to its end
/// position.
/// </summary>
internal sealed record CatchUpRead(ulong? FromExclusive, ulong EndInclusive, bool IsCompleted);