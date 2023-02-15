namespace DomainBlocks.Core.Builders;

public interface IAggregateTypeBuilder : IEventSourcedEntityTypeBuilder
{
    new IAggregateType Build();
}