using DomainBlocks.Persistence.Events;

namespace DomainBlocks.Persistence.Builders;

internal interface IEventTypeMappingBuilder
{
    IEnumerable<EventTypeMapping> Build();
}