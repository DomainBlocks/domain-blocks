using System;

namespace DomainBlocks.Projections.Builders;

/// <summary>
/// Explicitly implemented by <see cref="EventCatchUpSubscriptionOptionsBuilder"/> to hide methods that are used by
/// infrastructure specific extensions, but not intended to be called by application developers.
/// </summary>
public interface IEventCatchUpSubscriptionOptionsBuilderInfrastructure
{
    void WithEventDispatcherFactory(Func<ProjectionRegistry, IEventDispatcher> eventDispatcherFactory);
}