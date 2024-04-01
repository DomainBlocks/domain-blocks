namespace DomainBlocks.V1.Persistence.Builders;

internal interface IEventTypeMappingBuilder
{
    IEnumerable<EventTypeMapping> Build();
}