using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Internal.Subscriptions;

namespace DomainBlocks.EventStore.Internal.Tests.Unit.Subscriptions;

/// <summary>
/// Stands in for a store's snapshot. Its reads return no events, and each is noted as "read from {start} to {end}".
/// </summary>
internal sealed class TestHistorySnapshot(long? end, List<string> calls) : IHistorySnapshot<string>
{
    public long? End => end;

    public async IAsyncEnumerable<ReadEvent<string, string, StreamPosition, LogPosition>> ReadAsync(
        long startExclusive,
        long endInclusive,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        calls.Add($"read from {startExclusive} to {endInclusive}");
        await Task.CompletedTask;
        yield break;
    }
}