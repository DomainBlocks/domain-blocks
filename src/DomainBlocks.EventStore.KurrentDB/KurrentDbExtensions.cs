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
        // we request an "expectedStreamState", but kurrent returns an actual that will just be either StreamRevision, or NoStream.
        // The goal of this method is to convert from "WrongExpectedVersionException" to "WrongExpectedStreamStateException",
        // so we need to provide the actual stream state.
        var expectedState = ex.ExpectedStreamState.ToExpectedStreamState();
        var actualState = ex.ActualStreamState.ToExpectedStreamState();

        if (actualState.IsSpecificVersion && expectedState.IsSpecificVersion)
        {
            return WrongExpectedStreamStateException.VersionConflict(streamId, expectedState,
                StreamVersion.FromInt64(actualState.Version.Value.ToInt64()));
        }

        if (actualState == ExpectedStreamState.StreamDoesNotExist && expectedState.IsSpecificVersion)
        {
            return WrongExpectedStreamStateException.ExpectedStreamToExist(streamId);
        }

        if (actualState.IsSpecificVersion && expectedState == ExpectedStreamState.StreamDoesNotExist)
        {
            return WrongExpectedStreamStateException.ExpectedStreamToNotExist(streamId,
                actualState.Version.GetValueOrDefault());
        }

        if (actualState.IsSpecificVersion && expectedStreamState.IsAny)
        {
            // return WrongExpectedStreamStateException.VersionConflict();
        }

        return WrongExpectedStreamStateException.Unknown(streamId, expectedStreamState, ex);
    }
}