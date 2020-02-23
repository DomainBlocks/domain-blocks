namespace DomainLib.Aggregates
{
    /// <summary>
    /// Applies a domain event to an aggregate root, and returns the updated state.
    /// </summary>
    public delegate TAggregateRoot ApplyEvent<TAggregateRoot, in TDomainEventBase>(
        TAggregateRoot aggregate,
        TDomainEventBase @event);
}
