using EventStore.Client;

namespace DomainBlocks.Persistence.EventStoreDb;

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