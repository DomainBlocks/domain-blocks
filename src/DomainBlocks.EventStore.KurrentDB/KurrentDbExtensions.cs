using DomainBlocks.EventStore.Abstractions;
using KurrentDB.Client;

namespace DomainBlocks.EventStore.KurrentDB;

public static class KurrentDbExtensions
{
    public static WrongExpectedStreamStateException ToWrongExpectedStreamStateException(
        this WrongExpectedVersionException ex,
        string streamId,
        ExpectedStreamState expectedStreamState)
    {
        var actualState = ex.ActualStreamState.ToExpectedStreamState();

        if (expectedStreamState == ExpectedStreamState.StreamDoesNotExist &&
            actualState != ExpectedStreamState.StreamDoesNotExist)
        {
            return WrongExpectedStreamStateException.ExpectedStreamToNotExist(streamId, expectedStreamState.Version!.Value);
        }

        if (expectedStreamState == ExpectedStreamState.StreamExists &&
            actualState == ExpectedStreamState.StreamDoesNotExist)
        {
            return WrongExpectedStreamStateException.ExpectedStreamToExist(streamId);
        }

        var version = actualState.Version;
        if (version != expectedStreamState.Version)
        {
            return WrongExpectedStreamStateException.VersionConflict(streamId, expectedStreamState,
                actualState.Version!.Value);
        }

        // Not sure I like this part.
        throw ex;
    }
}