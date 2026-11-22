namespace DomainBlocks.EventStore.Tests.Chaos;

/// <summary>
/// Represents an invariant that a run broke, with the evidence.
/// </summary>
internal sealed record Violation(Invariant Invariant, string Evidence)
{
    public override string ToString() => $"{Invariant}: {Evidence}";
}