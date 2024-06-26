using DomainBlocks.V1.Abstractions;
using EventStore.Client;

namespace DomainBlocks.V1.EventStoreDb.Extensions;

internal static class ExpectedStreamStateExtensions
{
    public static StreamState ToEventStoreDbStreamState(this ExpectedStreamState expectedState)
    {
        return expectedState switch
        {
            ExpectedStreamState.Any => StreamState.Any,
            ExpectedStreamState.NoStream => StreamState.NoStream,
            _ => throw new ArgumentOutOfRangeException(nameof(expectedState), expectedState, null)
        };
    }
}