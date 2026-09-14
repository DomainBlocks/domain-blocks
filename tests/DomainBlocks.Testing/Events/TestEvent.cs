namespace DomainBlocks.Testing.Events;

public record TestEvent
{
    public required string Value { get; init; }
}