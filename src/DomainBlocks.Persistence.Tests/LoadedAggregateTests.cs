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
        var eventDispatcher = CreateEventDispatcher<MutableAggregate1>(MutableAggregate1.RegisterEvents);
        var aggregate = new MutableAggregate1(eventDispatcher);
        var loadedAggregate = CreateLoadedAggregate(aggregate, eventDispatcher);

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
        var eventDispatcher = CreateEventDispatcher<MutableAggregate2>(MutableAggregate2.RegisterEvents);
        var aggregate = new MutableAggregate2();
        var loadedAggregate = CreateLoadedAggregate(aggregate, eventDispatcher);

        const string commandData = "new state";
        loadedAggregate.ExecuteCommand((agg, dispatcher) => agg.Execute(commandData, dispatcher));
        var eventsToPersist = loadedAggregate.EventsToPersist.ToList();

        Assert.That(loadedAggregate.AggregateState, Is.SameAs(aggregate));
        Assert.That(loadedAggregate.AggregateState.State, Is.EqualTo(commandData));
        Assert.That(eventsToPersist, Has.Count.EqualTo(1));
        Assert.That(eventsToPersist[0], Is.TypeOf<StateChangedEvent>());
    }
    
    [Test]
    public void ImmutableAggregateScenario1()
    {
        var eventDispatcher = CreateEventDispatcher<ImmutableAggregate1>(ImmutableAggregate1.RegisterEvents);
        var aggregate = new ImmutableAggregate1(eventDispatcher);
        var loadedAggregate = CreateLoadedAggregate(aggregate, eventDispatcher);

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
        var eventDispatcher = CreateEventDispatcher<ImmutableAggregate2>(ImmutableAggregate2.RegisterEvents);
        var aggregate = new ImmutableAggregate2();
        var loadedAggregate = CreateLoadedAggregate(aggregate, eventDispatcher);

        const string commandData = "new state";
        loadedAggregate.ExecuteCommand((agg, dispatcher) => agg.Execute(commandData, dispatcher));
        var eventsToPersist = loadedAggregate.EventsToPersist.ToList();

        Assert.That(loadedAggregate.AggregateState, Is.Not.SameAs(aggregate));
        Assert.That(loadedAggregate.AggregateState.State, Is.EqualTo(commandData));
        Assert.That(eventsToPersist, Has.Count.EqualTo(1));
        Assert.That(eventsToPersist[0], Is.TypeOf<StateChangedEvent>());
    }
    
    [Test]
    public void ImmutableAggregateScenario3()
    {
        var eventDispatcher = CreateEventDispatcher<ImmutableAggregate3>(ImmutableAggregate3.RegisterEvents);
        var aggregate = new ImmutableAggregate3();
        var loadedAggregate = CreateLoadedAggregate(aggregate, eventDispatcher);

        const string commandData = "new state";
        loadedAggregate.ExecuteCommand(x => x.Execute(commandData));
        var eventsToPersist = loadedAggregate.EventsToPersist.ToList();

        Assert.That(loadedAggregate.AggregateState, Is.Not.SameAs(aggregate));
        Assert.That(loadedAggregate.AggregateState.State, Is.EqualTo(commandData));
        Assert.That(eventsToPersist, Has.Count.EqualTo(1));
        Assert.That(eventsToPersist[0], Is.TypeOf<StateChangedEvent>());
    }
    
    private static TrackingEventDispatcher<object> CreateEventDispatcher<TAggregate>(
        Action<EventRegistryBuilder<TAggregate, object>> builderAction)
    {
        var eventRegistry = EventRegistryBuilder.OfType<object>().For(builderAction).Build();
        return new TrackingEventDispatcher<object>(new EventDispatcher<object>(eventRegistry.EventRoutes));
    }

    private static LoadedAggregate<TAggregate, object> CreateLoadedAggregate<TAggregate>(
        TAggregate initialState, TrackingEventDispatcher<object> eventDispatcher)
    {
        return new LoadedAggregate<TAggregate, object>(initialState, "id", -1, null, 0, eventDispatcher);
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
        private readonly IEventDispatcher<object> _eventDispatcher;

        public MutableAggregate1(IEventDispatcher<object> eventDispatcher)
        {
            _eventDispatcher = eventDispatcher;
        }

        public string State { get; private set; }

        public static void RegisterEvents(EventRegistryBuilder<MutableAggregate1, object> events)
        {
            events.Event<StateChangedEvent>().RoutesTo((agg, e) => agg.Apply(e));
        }

        public void Execute(string newState)
        {
            _eventDispatcher.Dispatch(this, new StateChangedEvent(newState));
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

        public void Execute(string newState, IEventDispatcher<object> eventDispatcher)
        {
            eventDispatcher.Dispatch(this, new StateChangedEvent(newState));
        }

        private void Apply(StateChangedEvent e)
        {
            State = e.State;
        }
    }

    private class ImmutableAggregate1
    {
        private readonly IEventDispatcher<object> _eventDispatcher;

        public ImmutableAggregate1(IEventDispatcher<object> eventDispatcher)
        {
            _eventDispatcher = eventDispatcher;
        }

        private ImmutableAggregate1(string state, IEventDispatcher<object> eventDispatcher) : this(eventDispatcher)
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
            return _eventDispatcher.Dispatch(this, new StateChangedEvent(newState));
        }
        
        private ImmutableAggregate1 Apply(StateChangedEvent e)
        {
            return new ImmutableAggregate1(e.State, _eventDispatcher);
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
        
        public ImmutableAggregate2 Execute(string newState, IEventDispatcher<object> eventDispatcher)
        {
            return eventDispatcher.Dispatch(this, new StateChangedEvent(newState));
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