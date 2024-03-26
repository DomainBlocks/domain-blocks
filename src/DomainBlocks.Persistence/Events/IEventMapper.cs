namespace DomainBlocks.Persistence.Events;

public interface IEventMapper : IReadOnlyEventMapper, IWriteOnlyEventMapper
{
}