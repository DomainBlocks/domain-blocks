using System;
using System.Collections.Generic;
using System.Linq;
using DomainBlocks.Aggregates;

namespace DomainBlocks.Persistence
{
    internal static class LoadedAggregate
    {
        internal static LoadedAggregate<TAggregateState, TCommandBase, TEventBase>
            Create<TAggregateState, TCommandBase, TEventBase>(TAggregateState aggregateState,
                                                              string id,
                                                              CommandDispatcher<TCommandBase, TEventBase> commandDispatcher,
                                                              long version,
                                                              long? snapshotVersion,
                                                              long eventsLoaded)
        {
            return new LoadedAggregate<TAggregateState, TCommandBase, TEventBase>(
             aggregateState,
             id,
             commandDispatcher,
             version,
             snapshotVersion,
             eventsLoaded);
        }
    }

    public sealed class LoadedAggregate<TAggregateState, TCommandBase, TEventBase>
    {
        private readonly CommandDispatcher<TCommandBase, TEventBase> _commandDispatcher;

        internal LoadedAggregate(TAggregateState aggregateState,
                                 string id,
                                 CommandDispatcher<TCommandBase, TEventBase> commandDispatcher,
                                 long version,
                                 long? snapshotVersion,
                                 long eventsLoadedCount)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(id));
            _commandDispatcher = commandDispatcher ?? throw new ArgumentNullException(nameof(commandDispatcher));
            AggregateState = aggregateState ?? throw new ArgumentNullException(nameof(aggregateState));
            Id = id;
            Version = version;
            SnapshotVersion = snapshotVersion;
            EventsLoadedCount = eventsLoadedCount;
            EventsToPersist = Enumerable.Empty<TEventBase>();
        }

        public void DispatchCommand<TCommand>(TCommand command) where TCommand : TCommandBase
        {
            var events = _commandDispatcher.Dispatch(AggregateState, command);
            EventsToPersist = EventsToPersist.Concat(events);
        }

        public void ImmutableDispatchCommand<TCommand>(TCommand command) where TCommand : TCommandBase
        {
            var (newState, events) = _commandDispatcher.ImmutableDispatch(AggregateState, command);
            EventsToPersist = EventsToPersist.Concat(events);
            AggregateState = newState;
        }

        public string Id { get; }
        public TAggregateState AggregateState { get; private set; }
        public long Version { get; }
        public long? SnapshotVersion { get; }
        public long EventsLoadedCount { get; }
        public IEnumerable<TEventBase> EventsToPersist { get; private set; }
        internal bool HasBeenSaved { get; set; }
    }
}