using DomainBlocks.Experimental.Persistence.Events;

namespace DomainBlocks.Experimental.Persistence.Builders;

internal interface IEventTypeMappingBuilder
{
    EventTypeMappingBuilderKind Kind { get; }

    IEnumerable<EventTypeMapping> Build();
}