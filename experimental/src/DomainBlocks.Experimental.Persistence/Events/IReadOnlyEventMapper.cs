namespace DomainBlocks.Experimental.Persistence.Events;

public interface IReadOnlyEventMapper
{
    bool IsEventNameIgnored(string eventName);
    IEnumerable<object> FromReadEvent(ReadEvent readEvent);
}