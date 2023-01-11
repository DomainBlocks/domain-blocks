namespace DomainBlocks.Core.Serialization;

public interface IWriteEventAdapter<out TEvent>
{
    TEvent SerializeToWriteEvent(
        object @event,
        string eventName,
        IEnumerable<KeyValuePair<string, string>> metadata);
}