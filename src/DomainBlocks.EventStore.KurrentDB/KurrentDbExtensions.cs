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
        var actualState = ex.ActualStreamState.ToExpectedStreamState();

        if (expectedStreamState == ExpectedStreamState.StreamDoesNotExist &&
            actualState != ExpectedStreamState.StreamDoesNotExist)
        {
            // Version is supposed to exist here by virtue of how "StreamDoesNotExist" is constructed.
            if (expectedStreamState.Version != null)
            {
                return WrongExpectedStreamStateException.ExpectedStreamToNotExist(streamId,
                    expectedStreamState.Version.Value);
            }
        }

        if (expectedStreamState == ExpectedStreamState.StreamExists &&
            actualState == ExpectedStreamState.StreamDoesNotExist)
        {
            return WrongExpectedStreamStateException.ExpectedStreamToExist(streamId);
        }

        return WrongExpectedStreamStateException.VersionConflict(streamId, expectedStreamState,
            actualState.Version ?? StreamVersion.None);
    }
}