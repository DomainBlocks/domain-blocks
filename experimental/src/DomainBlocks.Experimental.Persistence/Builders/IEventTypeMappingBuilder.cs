using DomainBlocks.Experimental.Persistence.Events;

namespace DomainBlocks.Experimental.Persistence.Builders;

public interface IEventTypeMappingBuilder
{
    EventTypeMappingBuilderKind Kind { get; }

    IEnumerable<EventTypeMapping> Build();
}