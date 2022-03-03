using System;
using System.Collections.Generic;
using System.Linq;
using DomainBlocks.Aggregates.Registration;

namespace DomainBlocks.Aggregates
{
    /// <summary>
    /// Dispatches commands to an aggregate root, where they are executed and produce an updated aggregate state
    /// along with one or more domain events representing the changes that have occurred
    /// </summary>
    public sealed class CommandDispatcher<TCommandBase, TEventBase>
    {
        private readonly CommandRegistrations<TCommandBase, TEventBase> _registrations;
        private readonly EventDispatcher<TEventBase> _eventDispatcher;

        internal CommandDispatcher(
            CommandRegistrations<TCommandBase, TEventBase> registrations, EventDispatcher<TEventBase> eventDispatcher)
        {
            _registrations = registrations ?? throw new ArgumentNullException(nameof(registrations));
            _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
        }

        public bool HasImmutableRegistrations => _registrations.ImmutableRoutes.Any();

        public IReadOnlyList<TEventBase> Dispatch<TAggregate>(TAggregate aggregateRoot, TCommandBase command)
        {
            var commandType = command.GetType();
            var aggregateRootType = aggregateRoot.GetType();
            var routeKey = (aggregateRootType, commandType);

            if (_registrations.Routes.TryGetValue(routeKey, out var executeCommand))
            {
                _registrations.PreCommandHook?.Invoke(command);

                // The following code allows invokers of this method to yield return in order to see the side effect of
                // applying the event to the aggregate.
                var events = executeCommand(aggregateRoot, command)
                    .Select(x =>
                    {
                        _eventDispatcher.Dispatch(aggregateRoot, x);
                        return x;
                    })
                    .ToList();
                
                _registrations.PostCommandHook?.Invoke(command);
                
                return events;
            }

            var message = $"No route found when attempting to apply command " +
                          $"{commandType.Name} to {aggregateRootType.Name}";
            throw new InvalidOperationException(message);
        }

        public (TAggregate, IReadOnlyList<TEventBase>) ImmutableDispatch<TAggregate>(
            TAggregate aggregateRoot, TCommandBase command)
        {
            var commandType = command.GetType();
            var aggregateRootType = aggregateRoot.GetType();
            var routeKey = (aggregateRootType, commandType);
            
            if (_registrations.ImmutableRoutes.TryGetValue(routeKey, out var executeCommand))
            {
                _registrations.PreCommandHook?.Invoke(command);

                var initialResult = (newState: aggregateRoot, events: new List<TEventBase>());
                var currentState = aggregateRoot;

                var finalResult = executeCommand(() => currentState, command)
                    .Aggregate(initialResult, (result, @event) =>
                    {
                        var (state, events) = result;
                        var newState = _eventDispatcher.ImmutableDispatch(state, @event);
                        events.Add(@event);
                        var newResult = (newState, events);
                        currentState = newState;

                        return newResult;
                    });

                _registrations.PostCommandHook?.Invoke(command);

                return finalResult;
            }

            var message = $"No route found when attempting to apply command " +
                          $"{commandType.Name} to {aggregateRootType.Name}";
            throw new InvalidOperationException(message);
        }
    }
}