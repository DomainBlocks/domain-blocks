using DomainBlocks.EventStore.Abstractions;
using KurrentDB.Client;

namespace DomainBlocks.EventStore.KurrentDB;

internal static class KurrentDbExtensions
{
    internal static WrongExpectedStreamStateException ToWrongExpectedStreamStateException(
        this WrongExpectedVersionException ex,
        string streamId,
        ExpectedStreamState expectedStreamState)
    {
        // we request an "expectedStreamState", but kurrent returns an actual that will just be either StreamRevision,
        // or NoStream.The goal of this method is to convert from "WrongExpectedVersionException" to
        // "WrongExpectedStreamStateException", so we need to provide the actual stream state.
        var actualState = ex.ActualStreamState.ToExpectedStreamState();

        // When we expect "StreamDoesNotExist" & the stream does exist, Kurrent returns the actual stream version.
        if (expectedStreamState.IsStreamDoesNotExist
            && expectedStreamState.IsStreamDoesNotExist
            != actualState.IsStreamDoesNotExist)
        {
            var streamRevision = actualState.IsSpecificVersion
                ? actualState.Version.Value
                : StreamVersion.FromInt64(-1);
            return WrongExpectedStreamStateException.ExpectedStreamToNotExist(streamId, streamRevision);
        }

        if ((expectedStreamState.IsStreamExists || expectedStreamState.IsAny) && actualState.IsStreamDoesNotExist)
        {
            return WrongExpectedStreamStateException.ExpectedStreamToExist(streamId);
        }

        if (expectedStreamState.IsSpecificVersion)
        {
            // we have a version conflict. Either because the stream doesn't exist or the version is different.
            // So the stream revision is either None or the actual version.
            var actualStreamRevision = actualState.IsSpecificVersion ? actualState.Version.Value : StreamVersion.None;
            return WrongExpectedStreamStateException.VersionConflict(streamId,
                ex.ExpectedStreamState.ToExpectedStreamState(), actualStreamRevision);
        }

        return WrongExpectedStreamStateException.Unknown(streamId, expectedStreamState, ex);
    }
}