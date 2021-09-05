using System;

namespace DomainBlocks.Persistence
{
    public enum AggregateLoadStrategy
    {
        /// <summary>
        /// Use a snapshot to load the aggregate state and then append subsequent events from the event stream
        /// If a snapshot cannot be found, a <see cref="SnapshotDoesNotExistException"/> is thrown
        /// </summary>
        /// <exception cref="SnapshotDoesNotExistException"></exception>
        UseSnapshot,
        /// <summary>
        /// Usa a supplied initial state and then append events from the event stream
        /// </summary>
        UseEventStream,
        /// <summary>
        /// Use a snapshot to load the aggregate state if available.
        /// If a snapshot is found, then this will be used as the state onto which to append events.
        /// If no snapshot is found, then the user supplied state will be used and
        /// an <see cref="InvalidOperationException"/> will be thrown if this state is null.
        /// </summary>
        PreferSnapshot
    }
}