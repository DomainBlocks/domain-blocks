using DomainBlocks.EventStore.Abstractions;
using KurrentDB.Client;

namespace DomainBlocks.EventStore.KurrentDB;

public static class StreamStateExtensions
{
    public static StreamState ToKurrentDBStreamState(this ExpectedStreamState streamState)
    {
        if (streamState == ExpectedStreamState.Any)
            return StreamState.Any;
        if (streamState == ExpectedStreamState.StreamDoesNotExist)
            return StreamState.NoStream;
        if (streamState == ExpectedStreamState.StreamExists)
            return StreamState.StreamExists;
        return streamState.IsSpecificVersion
            ? StreamState.StreamRevision(streamState.Version.Value.ToUint64())
            : throw new ArgumentOutOfRangeException(nameof(streamState), "Unknown ExpectedStreamState");
    }

    public static ExpectedStreamState ToExpectedStreamState(this StreamState streamState)
    {
        if (streamState == StreamState.Any)
            return ExpectedStreamState.Any;
        if (streamState == StreamState.NoStream)
            return ExpectedStreamState.StreamDoesNotExist;
        if (streamState == StreamState.StreamExists)
            return ExpectedStreamState.StreamExists;
        return streamState.HasPosition
            ? ExpectedStreamState.FromVersion(StreamVersion.FromInt64(streamState.ToInt64())) : ExpectedStreamState.Any;
    }
}