using SqlStreamStoreStreamVersion = DomainBlocks.ThirdParty.SqlStreamStore.Streams.StreamVersion;

namespace DomainBlocks.Persistence.SqlStreamStore.Extensions;

internal static class StreamReadOriginExtensions
{
    public static int ToSqlStreamStoreStreamVersion(this StreamReadOrigin readOrigin, StreamReadDirection direction)
    {
        return readOrigin switch
        {
            StreamReadOrigin.Default => direction == StreamReadDirection.Forwards
                ? SqlStreamStoreStreamVersion.Start
                : SqlStreamStoreStreamVersion.End,
            StreamReadOrigin.Start => SqlStreamStoreStreamVersion.Start,
            StreamReadOrigin.End => SqlStreamStoreStreamVersion.End,
            _ => throw new ArgumentOutOfRangeException(nameof(readOrigin), readOrigin, null)
        };
    }
}