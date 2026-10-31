using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Internal.Subscriptions;
using Polly;

namespace DomainBlocks.EventStore.Internal.Tests.Unit.Subscriptions;

/// <summary>
/// Stands in for a store's snapshot. Each read is noted as "read from {start} to {end}", and returns the events at the
/// given positions within its range. The filter is taken to have selected only those positions. Disposal is noted as
/// "dispose".
/// </summary>
internal sealed class TestCatchUpSnapshot(
    SubscriptionOrigin<LogPosition> origin,
    long? end,
    ResiliencePipeline retryPolicy,
    List<string> calls,
    IReadOnlyList<long>? eventPositions = null) :
    CatchUpSnapshot<string, LogPosition>(origin, end, retryPolicy)
{
    public static ReadEvent<string, string, StreamPosition, LogPosition> CreateEvent(long position)
    {
        var context = ReadEventContext.Create(
            "stream-1",
            "OrderPlaced",
            DateTimeOffset.UnixEpoch,
            StreamPosition.FromInt64(position),
            LogPosition.FromInt64(position));

        return ReadEvent.Create("payload", new Dictionary<string, string>(), context);
    }

    protected override async IAsyncEnumerable<ReadEvent<string, string, StreamPosition, LogPosition>> ReadAsync(
        long startExclusive,
        long endInclusive,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        calls.Add($"read from {startExclusive} to {endInclusive}");
        await Task.CompletedTask;

        foreach (var position in eventPositions ?? [])
        {
            if (position > startExclusive && position <= endInclusive)
                yield return CreateEvent(position);
        }
    }

    protected override ValueTask DisposeAsyncCore()
    {
        calls.Add("dispose");
        return ValueTask.CompletedTask;
    }
}