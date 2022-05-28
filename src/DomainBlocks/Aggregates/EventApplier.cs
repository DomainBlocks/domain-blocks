namespace DomainBlocks.Aggregates
{
    /// <summary>
    /// Applies a domain event to an aggregate root, and returns the updated state.
    /// </summary>
    public delegate TAggregate EventApplier<TAggregate, in TEvent>(TAggregate aggregate, TEvent @event);
}