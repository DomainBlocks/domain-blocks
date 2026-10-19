using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// The latest operation time at which the store's subscription sources have read, shared by all of them.
/// </summary>
/// <remarks>
/// A source advances the fence after each response, before it hands that response's documents on, and advances its
/// causally consistent session to the fence before each read. A read then waits on the server until its node's
/// majority commit point has passed the fence, so a catch-up sees every event that the tail has already delivered, even
/// after a failover to a node whose commit point lags.
/// </remarks>
internal sealed class OperationTimeFence
{
    private BsonTimestamp? _time;

    public BsonTimestamp? Time => Volatile.Read(ref _time);

    public void Advance(BsonTimestamp? time)
    {
        if (time is null)
            return;

        var current = Volatile.Read(ref _time);

        // Sources advance the fence concurrently, so it moves only forwards, by compare-and-swap.
        while (current is null || time.CompareTo(current) > 0)
        {
            var previous = Interlocked.CompareExchange(ref _time, time, current);

            if (ReferenceEquals(previous, current))
                return;

            current = previous;
        }
    }
}