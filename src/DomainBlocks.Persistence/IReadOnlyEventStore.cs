using DomainBlocks.Persistence.Events;

namespace DomainBlocks.Persistence;

/// <summary>
/// Exposes read-only operations for a store of events.
/// </summary>
public interface IReadOnlyEventStore
{
    IAsyncEnumerable<ReadEvent> ReadStreamAsync(
        string streamName,
        StreamReadDirection direction,
        StreamVersion fromVersion,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ReadEvent> ReadStreamAsync(
        string streamName,
        StreamReadDirection direction,
        StreamReadOrigin readOrigin = StreamReadOrigin.Default,
        CancellationToken cancellationToken = default);
}