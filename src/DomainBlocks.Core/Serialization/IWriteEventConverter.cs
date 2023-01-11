namespace DomainBlocks.Core.Serialization;

public interface IWriteEventConverter<out TEvent>
{
    TEvent SerializeToWriteEvent(
        object @event,
        string? eventNameOverride = null,
        params KeyValuePair<string, string>[] additionalMetadata);
}