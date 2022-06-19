using System;
using System.Collections.Generic;
using System.Linq;
using DomainBlocks.Aggregates;
using NUnit.Framework;

namespace DomainBlocks.Tests.Aggregates;

public static class FuncExtensions
{
    public static (TState, IEnumerable<TEvent>) Apply<TState, TEvent>(
        this Func<TState, TEvent, TState> eventApplier,
        TState state,
        TEvent @event)
    {
        return eventApplier.Apply((state, Enumerable.Empty<TEvent>()), @event);
    }
    
    public static (TState, IEnumerable<TEvent>) Apply<TState, TEvent>(
        this Func<TState, TEvent, TState> eventApplier,
        (TState, IEnumerable<TEvent>) stateWithEvents,
        TEvent @event)
    {
        var (state, events) = stateWithEvents;
        
        IEnumerable<TEvent> Yield(TEvent e)
        {
            yield return e;
        }

        return (eventApplier(state, @event), events.Concat(Yield(@event)));
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
            return EventApplier.Apply(this, new StateChangedEvent(newState));
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
}