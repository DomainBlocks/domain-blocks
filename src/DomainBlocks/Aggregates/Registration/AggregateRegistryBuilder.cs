using System;
using System.Collections.Generic;

namespace DomainBlocks.Aggregates.Registration
{
    public static class AggregateRegistryBuilder
    {
        public static AggregateRegistryBuilder<TCommandBase, TEventBase> Create<TCommandBase, TEventBase>()
        {
            return new();
        }

        public static AggregateRegistryBuilder<object, object> Create()
        {
            return Create<object, object>();
        }
    }

    public sealed class AggregateRegistryBuilder<TCommandBase, TEventBase>
    {
        private readonly CommandRegistrations<TCommandBase, TEventBase> _commandRegistrations = new();
        private readonly EventRoutes<TEventBase> _eventRoutes = new();
        private readonly ImmutableEventRoutes<TEventBase> _immutableEventRoutes = new();
        private readonly EventNameMap _eventNameMap = new();
        private readonly AggregateMetadataMap _aggregateMetadataMap = new();

        public AggregateRegistry<TCommandBase, TEventBase> Build()
        {
            return new(_commandRegistrations, _eventRoutes, _immutableEventRoutes, _eventNameMap, _aggregateMetadataMap);
        }
        
        public void Register<TAggregate>(Action<AggregateRegistrationBuilder<TAggregate, TCommandBase, TEventBase>> buildAggregateRegistration)
        {
            if (buildAggregateRegistration == null) throw new ArgumentNullException(nameof(buildAggregateRegistration));
            buildAggregateRegistration(new AggregateRegistrationBuilder<TAggregate, TCommandBase, TEventBase>(this));
        }

        public void RegisterPreCommandHook(Action<TCommandBase> hook)
        {
            _commandRegistrations.PreCommandHook = hook;
        }

        public void RegisterPostCommandHook(Action<TCommandBase> hook)
        {
            _commandRegistrations.PostCommandHook = hook;
        }

        internal void RegisterCommandRoute<TAggregate, TCommand, TEvent>(
            ExecuteCommand<TAggregate, TCommand, TEvent> executeCommand) where TCommand : TCommandBase
        {
            _commandRegistrations.Routes.Add(
                (typeof(TAggregate), typeof(TCommand)),
                (agg, cmd) => (IEnumerable<TEventBase>) executeCommand((TAggregate) agg, (TCommand) cmd));
        }

        internal void RegisterCommandRoute<TAggregate, TCommand, TEvent>(
            ImmutableExecuteCommand<TAggregate, TCommand, TEvent> executeCommand) where TCommand : TCommandBase
        {
            _commandRegistrations.ImmutableRoutes.Add(
                (typeof(TAggregate), typeof(TCommand)),
                (agg, cmd) => (IEnumerable<TEventBase>) executeCommand(() => (TAggregate) agg(), (TCommand) cmd));
        }
        
        internal void RegisterEventRoute<TAggregate, TEvent>(ApplyEvent<TAggregate, TEvent> applyEvent) where TEvent: TEventBase
        {
            _eventRoutes.Add((typeof(TAggregate), typeof(TEvent)), (agg, e) => applyEvent((TAggregate)agg, (TEvent)e));
        }

        internal void RegisterEventRoute<TAggregate, TEvent>(ImmutableApplyEvent<TAggregate, TEvent> applyEvent) where TEvent: TEventBase
        {
            _immutableEventRoutes.Add((typeof(TAggregate), typeof(TEvent)), (agg, e) => applyEvent((TAggregate)agg, (TEvent)e));
        }

        internal void RegisterEventName<TEvent>(string eventName)
        {
            _eventNameMap.RegisterEvent<TEvent>(eventName);
        }

        internal void RegisterInitialStateFunc<TAggregate>(Func<TAggregate> getState)
        {
            var aggregateMetadata = GetOrAddAggregateMetadata<TAggregate>();
            aggregateMetadata.GetInitialState = () => getState();
        }

        internal void RegisterAggregateIdFunc<TAggregate>(Func<TAggregate, string> getId)
        {
            var aggregateMetadata = GetOrAddAggregateMetadata<TAggregate>();
            aggregateMetadata.GetIdentifier = o => getId((TAggregate) o);
        }

        internal void RegisterAggregateKey<TAggregate>(Func<string, string> getPersistenceKey)
        {
            var aggregateMetadata = GetOrAddAggregateMetadata<TAggregate>();
            aggregateMetadata.GetKeyFromIdentifier = getPersistenceKey;
        }

        internal void RegisterAggregateSnapshotKey<TAggregate>(Func<string, string> getSnapshotKey)
        {
            var aggregateMetadata = GetOrAddAggregateMetadata<TAggregate>();
            aggregateMetadata.GetSnapshotKeyFromIdentifier = getSnapshotKey;
        }

        private AggregateMetadata GetOrAddAggregateMetadata<TAggregate>()
        {
            var aggregateType = typeof(TAggregate);
            if (!_aggregateMetadataMap.TryGetValue(aggregateType, out var aggregateMetadata))
            {
                aggregateMetadata = new AggregateMetadata();
                _aggregateMetadataMap.Add(aggregateType, aggregateMetadata);
            }

            return aggregateMetadata;
        }
    }
}