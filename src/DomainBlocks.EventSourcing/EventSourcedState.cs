namespace DomainBlocks.EventSourcing;

public sealed class EventSourcedState<TState, TVersion>(TState value, Optional<TVersion> version)
    where TState : notnull
    where TVersion : notnull
{
    public TState Value { get; } = value;
    public Optional<TVersion> Version { get; } = version;
}