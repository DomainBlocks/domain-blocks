namespace DomainBlocks.V1.Abstractions;

/// <summary>
/// Exposes write-only operations for a store of events.
/// </summary>
public interface IWriteOnlyEventStore
{
    Task<StreamVersion?> AppendToStreamAsync(
        string streamName,
        IEnumerable<WriteEvent> events,
        StreamVersion expectedVersion,
        CancellationToken cancellationToken = default);

    Task<StreamVersion?> AppendToStreamAsync(
        string streamName,
        IEnumerable<WriteEvent> events,
        ExpectedStreamState expectedState,
        CancellationToken cancellationToken = default);
}