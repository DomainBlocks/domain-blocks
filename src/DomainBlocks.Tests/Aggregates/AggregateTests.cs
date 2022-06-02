using System;
using System.Collections.Generic;
using System.Linq;
using DomainBlocks.Aggregates;
using DomainBlocks.Aggregates.Builders;
using NUnit.Framework;

namespace DomainBlocks.Tests.Aggregates;

[TestFixture]
public class AggregateTests
{
    [Test]
    public void MutableAggregateScenario1()
    {
        var eventRoutes = BuildEventRoutes<MutableAggregate1>(MutableAggregate1.RegisterEvents);
        var aggregate = Aggregate.Create(eventRoutes, x => new MutableAggregate1(x));
        var initialState = aggregate.State;

        const string commandData = "new state";
        aggregate.ExecuteCommand(x => x.Execute(commandData));

        Assert.That(aggregate.State, Is.SameAs(initialState));
        Assert.That(aggregate.State.State, Is.EqualTo(commandData));
        Assert.That(aggregate.RaisedEvents, Has.Count.EqualTo(1));
        Assert.That(aggregate.RaisedEvents[0], Is.TypeOf<StateChangedEvent>());
    }
    
    [Test]
    public void MutableAggregateScenario2()
    {
        var eventRoutes = BuildEventRoutes<MutableAggregate2>(MutableAggregate2.RegisterEvents);
        var aggregate = Aggregate.Create(eventRoutes, _ => new MutableAggregate2());
        var initialState = aggregate.State;

        const string commandData = "new state";
        aggregate.ExecuteCommand((agg, router) => agg.Execute(commandData, router));
        var raisedEvents = aggregate.RaisedEvents.ToList();

        Assert.That(aggregate.State, Is.SameAs(initialState));
        Assert.That(aggregate.State.State, Is.EqualTo(commandData));
        Assert.That(raisedEvents, Has.Count.EqualTo(1));
        Assert.That(raisedEvents[0], Is.TypeOf<StateChangedEvent>());
    }
    
    [Test]
    public void ImmutableAggregateScenario1()
    {
        var eventRoutes = BuildEventRoutes<ImmutableAggregate1>(ImmutableAggregate1.RegisterEvents);
        var aggregate = Aggregate.Create(eventRoutes, x => new ImmutableAggregate1(x));
        var initialState = aggregate.State;

        const string commandData = "new state";
        aggregate.ExecuteCommand(agg => agg.Execute(commandData));
        var raisedEvents = aggregate.RaisedEvents.ToList();

        Assert.That(aggregate.State, Is.Not.SameAs(initialState));
        Assert.That(aggregate.State.State, Is.EqualTo(commandData));
        Assert.That(raisedEvents, Has.Count.EqualTo(1));
        Assert.That(raisedEvents[0], Is.TypeOf<StateChangedEvent>());
    }
    
    [Test]
    public void ImmutableAggregateScenario2()
    {
        var eventRoutes = BuildEventRoutes<ImmutableAggregate2>(ImmutableAggregate2.RegisterEvents);
        var aggregate = Aggregate.Create(eventRoutes, _ => new ImmutableAggregate2());
        var initialState = aggregate.State;

        const string commandData = "new state";
        aggregate.ExecuteCommand((agg, router) => agg.Execute(commandData, router));
        var raisedEvents = aggregate.RaisedEvents.ToList();

        Assert.That(aggregate.State, Is.Not.SameAs(initialState));
        Assert.That(aggregate.State.State, Is.EqualTo(commandData));
        Assert.That(raisedEvents, Has.Count.EqualTo(1));
        Assert.That(raisedEvents[0], Is.TypeOf<StateChangedEvent>());
    }
    
    [Test]
    public void ImmutableAggregateScenario3()
    {
        var eventRoutes = BuildEventRoutes<ImmutableAggregate3>(ImmutableAggregate3.RegisterEvents);
        var aggregate = Aggregate.Create(eventRoutes, _ => new ImmutableAggregate3());
        var initialState = aggregate.State;

        const string commandData = "new state";
        aggregate.ExecuteCommand(x => x.Execute(commandData));
        var raisedEvents = aggregate.RaisedEvents.ToList();

        Assert.That(aggregate.State, Is.Not.SameAs(initialState));
        Assert.That(aggregate.State.State, Is.EqualTo(commandData));
        Assert.That(raisedEvents, Has.Count.EqualTo(1));
        Assert.That(raisedEvents[0], Is.TypeOf<StateChangedEvent>());
    }
    
    private static EventRoutes<object> BuildEventRoutes<TAggregate>(
        Action<EventRegistryBuilder<TAggregate, object>> builderAction)
    {
        return EventRegistryBuilder.OfType<object>().For(builderAction).Build().EventRoutes;
    }
    
    private class StateChangedEvent
    {
        public StateChangedEvent(string state)
        {
            State = state;
        }

        public string State { get; }
    }

    private class MutableAggregate1
    {
        private readonly AggregateEventRouter<object> _eventRouter;

        public MutableAggregate1(AggregateEventRouter<object> eventRouter)
        {
            _eventRouter = eventRouter;
        }

        public string State { get; private set; }

        public static void RegisterEvents(EventRegistryBuilder<MutableAggregate1, object> events)
        {
            events.Event<StateChangedEvent>().RoutesTo((agg, e) => agg.Apply(e));
        }

        public void Execute(string newState)
        {
            _eventRouter.Send(this, new StateChangedEvent(newState));
        }

        private void Apply(StateChangedEvent e)
        {
            State = e.State;
        }
    }
    
    private class MutableAggregate2
    {
        public string State { get; private set; }

        public static void RegisterEvents(EventRegistryBuilder<MutableAggregate2, object> events)
        {
            events.Event<StateChangedEvent>().RoutesTo((agg, e) => agg.Apply(e));
        }

        public void Execute(string newState, AggregateEventRouter<object> eventRouter)
        {
            eventRouter.Send(this, new StateChangedEvent(newState));
        }

        private void Apply(StateChangedEvent e)
        {
            State = e.State;
        }
    }

    private class ImmutableAggregate1
    {
        private readonly AggregateEventRouter<object> _eventRouter;

        public ImmutableAggregate1(AggregateEventRouter<object> eventRouter)
        {
            _eventRouter = eventRouter;
        }

        private ImmutableAggregate1(string state, AggregateEventRouter<object> eventRouter) : this(eventRouter)
        {
            State = state;
        }

        public string State { get; }
        
        public static void RegisterEvents(EventRegistryBuilder<ImmutableAggregate1, object> events)
        {
            events.Event<StateChangedEvent>().RoutesTo((agg, e) => agg.Apply(e));
        }
        
        public ImmutableAggregate1 Execute(string newState)
        {
            return _eventRouter.Send(this, new StateChangedEvent(newState));
        }
        
        private ImmutableAggregate1 Apply(StateChangedEvent e)
        {
            return new ImmutableAggregate1(e.State, _eventRouter);
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

        public string State { get; }
        
        public static void RegisterEvents(EventRegistryBuilder<ImmutableAggregate2, object> events)
        {
            events.Event<StateChangedEvent>().RoutesTo((agg, e) => agg.Apply(e));
        }
        
        public ImmutableAggregate2 Execute(string newState, AggregateEventRouter<object> eventRouter)
        {
            return eventRouter.Send(this, new StateChangedEvent(newState));
        }
        
        private ImmutableAggregate2 Apply(StateChangedEvent e)
        {
            return new ImmutableAggregate2(e.State);
        }
    }
    
    private class ImmutableAggregate3
    {
        public ImmutableAggregate3()
        {
        }

        private ImmutableAggregate3(string state)
        {
            State = state;
        }

        public string State { get; }
        
        public static void RegisterEvents(EventRegistryBuilder<ImmutableAggregate3, object> events)
        {
            events.Event<StateChangedEvent>().RoutesTo((agg, e) => agg.Apply(e));
        }
        
        public IEnumerable<object> Execute(string newState)
        {
            yield return new StateChangedEvent(newState);
        }
        
        private ImmutableAggregate3 Apply(StateChangedEvent e)
        {
            return new ImmutableAggregate3(e.State);
        }
    }
}