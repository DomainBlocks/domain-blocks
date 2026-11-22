namespace DomainBlocks.EventStore.Tests.Chaos;

/// <summary>
/// Represents an event that the writer appends. It carries its own identity, so the checker can recognize it wherever
/// it appears.
/// </summary>
internal sealed record ChaosEvent
{
    public required long Operation { get; init; }

    public required int Index { get; init; }
}