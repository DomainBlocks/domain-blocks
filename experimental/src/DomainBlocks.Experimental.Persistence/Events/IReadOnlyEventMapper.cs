namespace DomainBlocks.Experimental.Persistence.Events;

public interface IReadOnlyEventMapper
{
    IEnumerable<object> FromReadEvent(ReadEvent readEvent);
}