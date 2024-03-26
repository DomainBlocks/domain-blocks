namespace DomainBlocks.Persistence;

/// <summary>
/// Exposes both read and write operations for a store of events.
/// </summary>
public interface IEventStore : IReadOnlyEventStore, IWriteOnlyEventStore
{
}