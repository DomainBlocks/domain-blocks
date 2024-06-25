using DomainBlocks.V1.Abstractions;
using StreamPosition = EventStore.Client.StreamPosition;

namespace DomainBlocks.V1.EventStoreDb.Extensions;

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