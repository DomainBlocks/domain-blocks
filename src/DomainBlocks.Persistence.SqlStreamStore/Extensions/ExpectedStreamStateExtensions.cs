using DomainBlocks.Abstractions;
using DomainBlocks.ThirdParty.SqlStreamStore.Streams;

namespace DomainBlocks.Persistence.SqlStreamStore.Extensions;

internal static class ExpectedStreamStateExtensions
{
    public static int ToSqlStreamStoreExpectedVersion(this ExpectedStreamState expectedState)
    {
        return expectedState switch
        {
            ExpectedStreamState.Any => ExpectedVersion.Any,
            ExpectedStreamState.NoStream => ExpectedVersion.NoStream,
            _ => throw new ArgumentOutOfRangeException(nameof(expectedState), expectedState, null)
        };
    }
}