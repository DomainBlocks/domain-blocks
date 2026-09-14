namespace DomainBlocks.Testing;

public record TestEvent
{
    public required string Value { get; init; }
}