namespace DomainBlocks.Experimental.EventSourcing.Persistence.Adapters;

public interface IWriteEventAdapter<out TWriteEvent, in TRawData>
{
    TWriteEvent CreateWriteEvent(
        string eventName, TRawData data, TRawData? metadata = default, string? contentType = null);
}