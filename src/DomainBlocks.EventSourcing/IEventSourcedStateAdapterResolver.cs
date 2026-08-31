namespace DomainBlocks.EventSourcing;

/// <summary>
/// Resolves event-sourced state adapters by state type.
/// </summary>
/// <typeparam name="TEvent">The event type used by the resolved adapters.</typeparam>
/// <typeparam name="TStreamId">The type used to identify event streams.</typeparam>
public interface IEventSourcedStateAdapterResolver<TEvent, out TStreamId>
    where TEvent : notnull
    where TStreamId : notnull
{
    /// <summary>
    /// Resolves an adapter for the specified state type.
    /// </summary>
    /// <typeparam name="TState">The state type for which to resolve an adapter.</typeparam>
    /// <returns>
    /// The matching adapter, or <see langword="null"/> if no adapter could be resolved.
    /// </returns>
    IEventSourcedStateAdapter<TState, TEvent, TStreamId>? Resolve<TState>() where TState : notnull;
}