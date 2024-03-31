namespace DomainBlocks.Persistence.Builders;

internal interface IEventTypeMappingBuilder
{
    IEnumerable<EventTypeMapping> Build();
}