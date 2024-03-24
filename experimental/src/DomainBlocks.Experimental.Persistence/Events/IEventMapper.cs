namespace DomainBlocks.Experimental.Persistence.Events;

public interface IEventMapper : IReadOnlyEventMapper, IWriteOnlyEventMapper
{
}