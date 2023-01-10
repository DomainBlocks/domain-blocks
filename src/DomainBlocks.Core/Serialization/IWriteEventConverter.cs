namespace DomainBlocks.Core.Serialization;

public interface IWriteEventConverter<out TWriteEvent>
{
    TWriteEvent SerializeToWriteEvent(
        object @event,
        string? eventNameOverride = null,
        params KeyValuePair<string, string>[] additionalMetadata);
}