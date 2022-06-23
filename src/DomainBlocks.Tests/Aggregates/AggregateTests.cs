using System;
using System.Collections.Generic;
using System.Linq;
using DomainBlocks.Aggregates;
using NUnit.Framework;

namespace DomainBlocks.Tests.Aggregates;

public static class FuncExtensions
{
    public static (TState, IEnumerable<TEventBase>) Apply<TState, TEventBase>(
        this Func<TState, TEventBase, TState> eventApplier,
        TState state,
        TEventBase @event)
    {
        return eventApplier.Apply((state, Enumerable.Empty<TEventBase>()), @event);
    }
    
    public static (TState, IEnumerable<TEventBase>) Apply<TState, TEventBase>(
        this Func<TState, TEventBase, TState> eventApplier,
        (TState, IEnumerable<TEventBase>) stateWithEvents,
        TEventBase @event)
    {
        var (state, events) = stateWithEvents;
        
        IEnumerable<TEventBase> Return(TEventBase e)
        {
            yield return e;
        }

        return (eventApplier(state, @event), events.Concat(Return(@event)));
    }
}

[TestFixture]
public class AggregateTests
{
    [Test]
    public void MutableAggregateScenario()
    {
        var initialState = new MutableAggregate();
        var aggregate = Aggregate.Create(initialState, MutableAggregate.EventApplier);

        const string commandData = "new state";
        aggregate.ExecuteCommand(s => s.Execute(commandData), s => s.AppliedEvents);

        Assert.That(aggregate.State, Is.SameAs(initialState));
        Assert.That(aggregate.State.State, Is.EqualTo(commandData));
        Assert.That(aggregate.AppliedEvents, Has.Count.EqualTo(1));
        Assert.That(aggregate.AppliedEvents[0], Is.TypeOf<StateChangedEvent>());
    }

    [Test]
    public void ImmutableAggregateScenario1()
    {
        var initialState = new ImmutableAggregate1();
        var aggregate = Aggregate.Create(initialState, ImmutableAggregate1.EventApplier);

        const string commandData = "new state";

        aggregate.ExecuteCommand(s => s.Execute(commandData));

        Assert.That(aggregate.State, Is.Not.SameAs(initialState));
        Assert.That(aggregate.State.State, Is.EqualTo(commandData));
        Assert.That(aggregate.AppliedEvents, Has.Count.EqualTo(1));
        Assert.That(aggregate.AppliedEvents[0], Is.TypeOf<StateChangedEvent>());
    }

    [Test]
    public void ImmutableAggregateScenario2()
    {
        var initialState = new ImmutableAggregate2();
        var aggregate = Aggregate.Create(initialState, ImmutableAggregate2.EventApplier);

        const string commandData = "new state";
        aggregate.ExecuteCommand(s => s.Execute(commandData));

        Assert.That(aggregate.State, Is.Not.SameAs(initialState));
        Assert.That(aggregate.State.State, Is.EqualTo(commandData));
        Assert.That(aggregate.AppliedEvents, Has.Count.EqualTo(1));
        Assert.That(aggregate.AppliedEvents[0], Is.TypeOf<StateChangedEvent>());
    }
    
    [Test]
    public void ImmutableAggregateScenario3()
    {
        var initialState = new ImmutableAggregate3();
        var aggregate = Aggregate.Create(initialState, ImmutableAggregate3.EventApplier);

        const string commandData = "new state";
        var result = aggregate.ExecuteCommand(s => s.Execute(commandData), x => x.Events);

        Assert.That(aggregate.State, Is.Not.SameAs(initialState));
        Assert.That(aggregate.State.State, Is.EqualTo(commandData));
        Assert.That(aggregate.AppliedEvents, Has.Count.EqualTo(1));
        Assert.That(aggregate.AppliedEvents[0], Is.TypeOf<StateChangedEvent>());
    }

    public interface IEvent
    {
    }

    private class StateChangedEvent : IEvent
    {
        public StateChangedEvent(string state)
        {
            State = state;
        }

        public string State { get; }
    }

    private class MutableAggregate
    {
        private readonly List<IEvent> _appliedEvents = new();

        public static Action<MutableAggregate, IEvent> EventApplier => (s, e) =>
        {
            s.Apply((dynamic)e);
            s._appliedEvents.Add(e);
        };

        public string State { get; private set; }

        public IReadOnlyList<IEvent> AppliedEvents => _appliedEvents.AsReadOnly();

        public void Execute(string data)
        {
            EventApplier(this, new StateChangedEvent(data));
        }

        private void Apply(StateChangedEvent e)
        {
            State = e.State;
        }
    }
    
    public class ImmutableAggregate1
    {
        public ImmutableAggregate1()
        {
        }

        private ImmutableAggregate1(string state)
        {
            State = state;
        }

        public static Func<ImmutableAggregate1, IEvent, ImmutableAggregate1> EventApplier =>
            (s, e) => s.Apply((dynamic)e);

        public string State { get; }

        public (ImmutableAggregate1, IEnumerable<IEvent>) Execute(string newState)
        {
            var (state, events) = EventApplier.Apply(this, new StateChangedEvent(newState));
            
            (state, events) = EventApplier.Apply((state, events), new StateChangedEvent(newState));

            return (state, events);

            // var events = new List<IEvent>();

            // var stateChanged = new StateChangedEvent(newState);
            // var state = Apply(stateChanged);
            //
            // // Observe state change - make a decision
            //
            // events.Add(stateChanged);
            //
            // return (state, events);
        }

        private ImmutableAggregate1 Apply(StateChangedEvent e)
        {
            return new ImmutableAggregate1(e.State);
        }
    }

    private class ImmutableAggregate2
    {
        public ImmutableAggregate2()
        {
        }

        private ImmutableAggregate2(string state)
        {
            State = state;
        }

        public static Func<ImmutableAggregate2, IEvent, ImmutableAggregate2> EventApplier =>
            (s, e) => s.Apply((dynamic)e);

        public string State { get; }

        public IEnumerable<IEvent> Execute(string newState)
        {
            yield return new StateChangedEvent(newState);
        }

        private ImmutableAggregate2 Apply(StateChangedEvent e)
        {
            return new ImmutableAggregate2(e.State);
        }
    }

    public record Result(List<IEvent> Events);
    
    private class ImmutableAggregate3
    {
        public ImmutableAggregate3()
        {
        }

        private ImmutableAggregate3(string state)
        {
            State = state;
        }

        public static Func<ImmutableAggregate3, IEvent, ImmutableAggregate3> EventApplier =>
            (s, e) => s.Apply((dynamic)e);

        public string State { get; }

        public Result Execute(string newState)
        {
            var events = new List<IEvent>();
            var stateChanged = new StateChangedEvent("foo");
            var state = Apply(stateChanged);
            events.Add(stateChanged);

            return new Result(events);
        }

        private ImmutableAggregate3 Apply(StateChangedEvent e)
        {
            return new ImmutableAggregate3(e.State);
        }
    }
}