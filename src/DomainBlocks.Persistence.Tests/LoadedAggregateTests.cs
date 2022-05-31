using System;
using System.Collections.Generic;
using System.Linq;
using DomainBlocks.Aggregates;
using DomainBlocks.Aggregates.Builders;
using NUnit.Framework;

namespace DomainBlocks.Persistence.Tests;

[TestFixture]
public class LoadedAggregateTests
{
    [Test]
    public void MutableAggregateScenario1()
    {
        var eventRouter = CreateEventRouter<MutableAggregate1>(MutableAggregate1.RegisterEvents);
        var aggregate = new MutableAggregate1(eventRouter);
        var loadedAggregate = CreateLoadedAggregate(aggregate, eventRouter);

        const string commandData = "new state";
        loadedAggregate.ExecuteCommand(x => x.Execute(commandData));
        var eventsToPersist = loadedAggregate.EventsToPersist.ToList();

        Assert.That(loadedAggregate.AggregateState, Is.SameAs(aggregate));
        Assert.That(loadedAggregate.AggregateState.State, Is.EqualTo(commandData));
        Assert.That(eventsToPersist, Has.Count.EqualTo(1));
        Assert.That(eventsToPersist[0], Is.TypeOf<StateChangedEvent>());
    }
    
    [Test]
    public void MutableAggregateScenario2()
    {
        var eventRouter = CreateEventRouter<MutableAggregate2>(MutableAggregate2.RegisterEvents);
        var aggregate = new MutableAggregate2();
        var loadedAggregate = CreateLoadedAggregate(aggregate, eventRouter);

        const string commandData = "new state";
        loadedAggregate.ExecuteCommand((agg, router) => agg.Execute(commandData, router));
        var eventsToPersist = loadedAggregate.EventsToPersist.ToList();

        Assert.That(loadedAggregate.AggregateState, Is.SameAs(aggregate));
        Assert.That(loadedAggregate.AggregateState.State, Is.EqualTo(commandData));
        Assert.That(eventsToPersist, Has.Count.EqualTo(1));
        Assert.That(eventsToPersist[0], Is.TypeOf<StateChangedEvent>());
    }
    
    [Test]
    public void ImmutableAggregateScenario1()
    {
        var eventRouter = CreateEventRouter<ImmutableAggregate1>(ImmutableAggregate1.RegisterEvents);
        var aggregate = new ImmutableAggregate1(eventRouter);
        var loadedAggregate = CreateLoadedAggregate(aggregate, eventRouter);

        const string commandData = "new state";
        loadedAggregate.ExecuteCommand(agg => agg.Execute(commandData));
        var eventsToPersist = loadedAggregate.EventsToPersist.ToList();

        Assert.That(loadedAggregate.AggregateState, Is.Not.SameAs(aggregate));
        Assert.That(loadedAggregate.AggregateState.State, Is.EqualTo(commandData));
        Assert.That(eventsToPersist, Has.Count.EqualTo(1));
        Assert.That(eventsToPersist[0], Is.TypeOf<StateChangedEvent>());
    }
    
    [Test]
    public void ImmutableAggregateScenario2()
    {
        var eventRouter = CreateEventRouter<ImmutableAggregate2>(ImmutableAggregate2.RegisterEvents);
        var aggregate = new ImmutableAggregate2();
        var loadedAggregate = CreateLoadedAggregate(aggregate, eventRouter);

        const string commandData = "new state";
        loadedAggregate.ExecuteCommand((agg, router) => agg.Execute(commandData, router));
        var eventsToPersist = loadedAggregate.EventsToPersist.ToList();

        Assert.That(loadedAggregate.AggregateState, Is.Not.SameAs(aggregate));
        Assert.That(loadedAggregate.AggregateState.State, Is.EqualTo(commandData));
        Assert.That(eventsToPersist, Has.Count.EqualTo(1));
        Assert.That(eventsToPersist[0], Is.TypeOf<StateChangedEvent>());
    }
    
    [Test]
    public void ImmutableAggregateScenario3()
    {
        var eventRouter = CreateEventRouter<ImmutableAggregate3>(ImmutableAggregate3.RegisterEvents);
        var aggregate = new ImmutableAggregate3();
        var loadedAggregate = CreateLoadedAggregate(aggregate, eventRouter);

        const string commandData = "new state";
        loadedAggregate.ExecuteCommand(x => x.Execute(commandData));
        var eventsToPersist = loadedAggregate.EventsToPersist.ToList();

        Assert.That(loadedAggregate.AggregateState, Is.Not.SameAs(aggregate));
        Assert.That(loadedAggregate.AggregateState.State, Is.EqualTo(commandData));
        Assert.That(eventsToPersist, Has.Count.EqualTo(1));
        Assert.That(eventsToPersist[0], Is.TypeOf<StateChangedEvent>());
    }
    
    private static TrackingAggregateEventRouter<object> CreateEventRouter<TAggregate>(
        Action<EventRegistryBuilder<TAggregate, object>> builderAction)
    {
        var eventRegistry = EventRegistryBuilder.OfType<object>().For(builderAction).Build();
        return new TrackingAggregateEventRouter<object>(eventRegistry.EventRouter);
    }

    private static LoadedAggregate<TAggregate, object> CreateLoadedAggregate<TAggregate>(
        TAggregate initialState, TrackingAggregateEventRouter<object> eventRouter)
    {
        return new LoadedAggregate<TAggregate, object>(initialState, "id", -1, null, 0, eventRouter);
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
        private readonly IAggregateEventRouter<object> _eventRouter;

        public MutableAggregate1(IAggregateEventRouter<object> eventRouter)
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

        public void Execute(string newState, IAggregateEventRouter<object> eventRouter)
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
        private readonly IAggregateEventRouter<object> _eventRouter;

        public ImmutableAggregate1(IAggregateEventRouter<object> eventRouter)
        {
            _eventRouter = eventRouter;
        }

        private ImmutableAggregate1(string state, IAggregateEventRouter<object> eventRouter) : this(eventRouter)
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
        
        public ImmutableAggregate2 Execute(string newState, IAggregateEventRouter<object> eventRouter)
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