using DomainBlocks.Persistence.Events;

namespace DomainBlocks.Persistence.Builders;

internal interface IEventTypeMappingBuilder
{
    EventTypeMappingBuilderKind Kind { get; }

    IEnumerable<EventTypeMapping> Build();
}