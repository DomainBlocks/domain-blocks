namespace DomainBlocks.V1.Abstractions;

/// <summary>
/// Exposes write-only operations for a store of events.
/// </summary>
public interface IWriteOnlyEventStore
{
    Task<StreamPosition?> AppendToStreamAsync(
        string streamName,
        IEnumerable<WritableEventEntry> events,
        StreamPosition expectedVersion,
        CancellationToken cancellationToken = default);

    Task<StreamPosition?> AppendToStreamAsync(
        string streamName,
        IEnumerable<WritableEventEntry> events,
        ExpectedStreamState expectedState,
        CancellationToken cancellationToken = default);
}