namespace DomainBlocks.Testing.Integration;

public record TestEvent
{
    public required string Value { get; init; }
}