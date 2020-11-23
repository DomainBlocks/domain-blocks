namespace DomainLib.Aggregates
{
    /// <summary>
    /// Applies a domain event to a mutable aggregate root.
    /// </summary>
    public delegate void ApplyEvent<in TAggregate, in TEvent>(TAggregate aggregate, TEvent @event);
}