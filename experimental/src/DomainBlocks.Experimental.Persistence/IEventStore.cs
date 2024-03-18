namespace DomainBlocks.Experimental.Persistence;

/// <summary>
/// Exposes both read and write operations for a store of events.
/// </summary>
public interface IEventStore<out TReadEvent, in TWriteEvent> :
    IReadOnlyEventStore<TReadEvent>,
    IWriteOnlyEventStore<TWriteEvent>
{
}