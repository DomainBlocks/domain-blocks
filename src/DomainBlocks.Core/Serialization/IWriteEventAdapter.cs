namespace DomainBlocks.Core.Serialization;

public interface IWriteEventAdapter<out TWriteEvent>
{
    TWriteEvent SerializeToWriteEvent(
        object @event,
        string eventName,
        IEnumerable<KeyValuePair<string, string>> metadata);
}