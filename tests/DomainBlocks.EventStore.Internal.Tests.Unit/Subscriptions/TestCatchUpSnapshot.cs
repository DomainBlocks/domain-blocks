using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Internal.Subscriptions;

namespace DomainBlocks.EventStore.Internal.Tests.Unit.Subscriptions;

/// <summary>
/// Stands in for a store's snapshot. Its reads return no events, and each is noted as "read from {start} to {end}".
/// </summary>
internal sealed class TestCatchUpSnapshot(SubscriptionOrigin<LogPosition> origin, long? end, List<string> calls) :
    CatchUpSnapshot<string, LogPosition>(origin, end)
{
    protected override async IAsyncEnumerable<ReadEvent<string, string, StreamPosition, LogPosition>> ReadCoreAsync(
        long startExclusive,
        long endInclusive,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        calls.Add($"read from {startExclusive} to {endInclusive}");
        await Task.CompletedTask;
        yield break;
    }
}