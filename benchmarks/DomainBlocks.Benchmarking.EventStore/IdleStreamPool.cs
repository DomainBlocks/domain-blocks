using System.Collections.Concurrent;

namespace DomainBlocks.Benchmarking.EventStore;

/// <summary>
/// Hands out streams that have no append in flight, for open-loop appends, so that each append can expect the stream's
/// version, as an aggregate's append would, and never conflicts. A stream taken from the pool goes back when its
/// append completes, and it retires once it holds a set number of events.
/// </summary>
/// <remarks>
/// Each slot is a worker of a <see cref="StreamIdGenerator"/>, so streams are named and laid out as the closed-loop
/// workers' are. The queue hands each idle slot to one taker, so a slot is used by one append at a time, and the
/// generator's state for it needs no lock. The stream that has been idle longest is taken first.
/// </remarks>
public sealed class IdleStreamPool(int capacity, int eventsPerStream)
{
    private readonly StreamIdGenerator _streams = new(capacity, eventsPerStream);
    private readonly ConcurrentQueue<int> _idle = new();
    private int _slotsUsed;

    /// <summary>
    /// Takes an idle stream, or starts a new one if none is idle.
    /// </summary>
    /// <exception cref="InvalidOperationException">More than the pool's capacity of streams are taken.</exception>
    public Lease Take()
    {
        if (!_idle.TryDequeue(out var slot))
        {
            // A stream being returned while this runs can be missed, and a new slot used instead. That only starts a
            // new stream: every slot in use belongs to a lease not yet returned, so the slots in use never outnumber
            // the leases taken at once.
            slot = Interlocked.Increment(ref _slotsUsed) - 1;

            if (slot >= capacity)
                throw new InvalidOperationException($"More than {capacity:N0} streams are taken at once.");
        }

        var streamId = _streams.Next(slot, out var eventCount);
        return new Lease(slot, streamId, eventCount);
    }

    public void Return(Lease lease) => _idle.Enqueue(lease.Slot);

    /// <param name="Slot">The slot the stream was taken from.</param>
    /// <param name="StreamId">The stream's ID.</param>
    /// <param name="EventCount">The number of events the stream holds, before the append the lease is for.</param>
    public readonly record struct Lease(int Slot, string StreamId, int EventCount);
}