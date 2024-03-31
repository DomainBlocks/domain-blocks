using DomainBlocks.Abstractions;
using EventStore.Client;

namespace DomainBlocks.Persistence.EventStoreDb.Extensions;

internal static class StreamReadOriginExtensions
{
    public static StreamPosition ToEventStoreDbStreamPosition(
        this StreamReadOrigin readOrigin, StreamReadDirection direction)
    {
        return readOrigin switch
        {
            StreamReadOrigin.Default => direction == StreamReadDirection.Forwards
                ? StreamPosition.Start
                : StreamPosition.End,
            StreamReadOrigin.Start => StreamPosition.Start,
            StreamReadOrigin.End => StreamPosition.End,
            _ => throw new ArgumentOutOfRangeException(nameof(readOrigin), readOrigin, null)
        };
    }
}