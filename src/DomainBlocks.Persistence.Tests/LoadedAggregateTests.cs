﻿using System.Collections.Generic;
using System.Linq;
using DomainBlocks.Persistence.New;
using DomainBlocks.Persistence.New.Builders;
using NUnit.Framework;

namespace DomainBlocks.Persistence.Tests;

[TestFixture]
public class LoadedAggregateTests
{
    [Test]
    public void MutableAggregateScenario1()
    {
        var aggregateTypeBuilder = new AggregateTypeBuilder<MutableAggregate1, object>();

        aggregateTypeBuilder
            .VoidCommandResult()
            .WithEventsFrom(agg => agg.RaisedEvents)
            .WithUpdatedStateFrom(agg => agg);

        var aggregateType = aggregateTypeBuilder.Build();

        var aggregate = new MutableAggregate1();
        var loadedAggregate = CreateLoadedAggregate(aggregate, aggregateType);

        const string value = "new value";
        loadedAggregate.ExecuteCommand(x => x.Execute(value));
        var eventsToPersist = loadedAggregate.EventsToPersist.ToList();

        Assert.That(loadedAggregate.AggregateState, Is.SameAs(aggregate));
        Assert.That(loadedAggregate.AggregateState.Value, Is.EqualTo(value));
        Assert.That(eventsToPersist, Has.Count.EqualTo(1));
        Assert.That(eventsToPersist[0], Is.TypeOf<ValueChangedEvent>());
    }

    [Test]
    public void MutableAggregateScenario2()
    {
        var aggregateTypeBuilder = new AggregateTypeBuilder<MutableAggregate2, object>();
        aggregateTypeBuilder.ApplyEventsWith((agg, e) => agg.Apply(e));

        aggregateTypeBuilder
            .CommandResult<IEnumerable<object>>()
            .WithEventsFrom((res, _) => res)
            .ApplyEventsWhileEnumerating();

        var aggregateType = aggregateTypeBuilder.Build();

        var aggregate = new MutableAggregate2();
        var loadedAggregate = CreateLoadedAggregate(aggregate, aggregateType);

        const string value = "new value";
        loadedAggregate.ExecuteCommand(x => x.Execute(value));
        var eventsToPersist = loadedAggregate.EventsToPersist.ToList();

        Assert.That(loadedAggregate.AggregateState, Is.SameAs(aggregate));
        Assert.That(loadedAggregate.AggregateState.ObservedValues, Has.Count.EqualTo(3));
        Assert.That(loadedAggregate.AggregateState.ObservedValues[0], Is.EqualTo($"{value} 1"));
        Assert.That(loadedAggregate.AggregateState.ObservedValues[1], Is.EqualTo($"{value} 1 2"));
        Assert.That(loadedAggregate.AggregateState.ObservedValues[2], Is.EqualTo($"{value} 1 2 3"));
        Assert.That(eventsToPersist, Has.Count.EqualTo(3));
        Assert.That(eventsToPersist[0], Is.TypeOf<ValueChangedEvent>());
        Assert.That(eventsToPersist[1], Is.TypeOf<ValueChangedEvent>());
        Assert.That(eventsToPersist[2], Is.TypeOf<ValueChangedEvent>());
    }

    [Test]
    public void ImmutableAggregateScenario1()
    {
        var aggregateTypeBuilder = new AggregateTypeBuilder<ImmutableAggregate1, object>();
        aggregateTypeBuilder.ApplyEventsWith((agg, e) => agg.Apply(e));

        aggregateTypeBuilder
            .CommandResult<IEnumerable<object>>()
            .WithEventsFrom((res, _) => res)
            .ApplyEvents();

        var aggregateType = aggregateTypeBuilder.Build();

        var aggregate = new ImmutableAggregate1();
        var loadedAggregate = CreateLoadedAggregate(aggregate, aggregateType);

        const string commandData = "new state";
        loadedAggregate.ExecuteCommand(x => x.Execute(commandData));
        var eventsToPersist = loadedAggregate.EventsToPersist.ToList();

        Assert.That(loadedAggregate.AggregateState, Is.Not.SameAs(aggregate));
        Assert.That(loadedAggregate.AggregateState.Value, Is.EqualTo(commandData));
        Assert.That(eventsToPersist, Has.Count.EqualTo(1));
        Assert.That(eventsToPersist[0], Is.TypeOf<ValueChangedEvent>());
    }

    private static LoadedAggregate<TAggregate, object> CreateLoadedAggregate<TAggregate>(
        TAggregate initialState, AggregateType<TAggregate, object> aggregateType)
    {
        return new LoadedAggregate<TAggregate, object>(initialState, "id", -1, null, 0, aggregateType);
    }

    private class ValueChangedEvent
    {
        public ValueChangedEvent(string value)
        {
            Value = value;
        }

        public string Value { get; }
    }

    private class MutableAggregate1
    {
        private readonly List<object> _raisedEvents = new();

        public string Value { get; private set; }
        public IEnumerable<object> RaisedEvents => _raisedEvents;

        public void Execute(string newValue)
        {
            var @event = new ValueChangedEvent(newValue);
            Apply(@event);
            _raisedEvents.Add(@event);
        }

        private void Apply(ValueChangedEvent e)
        {
            Value = e.Value;
        }
    }

    private class MutableAggregate2
    {
        public string Value { get; private set; }
        public List<string> ObservedValues { get; } = new();

        public IEnumerable<object> Execute(string newValue)
        {
            yield return new ValueChangedEvent($"{newValue} 1");
            yield return new ValueChangedEvent($"{Value} 2");
            yield return new ValueChangedEvent($"{Value} 3");
        }

        public void Apply(object @event)
        {
            if (@event is ValueChangedEvent e)
            {
                Value = e.Value;
                ObservedValues.Add(e.Value);
            }
        }
    }

    private class ImmutableAggregate1
    {
        public ImmutableAggregate1()
        {
        }

        private ImmutableAggregate1(string value)
        {
            Value = value;
        }

        public string Value { get; }

        public IEnumerable<object> Execute(string newValue)
        {
            yield return new ValueChangedEvent(newValue);
        }

        public ImmutableAggregate1 Apply(object @event)
        {
            return @event switch
            {
                ValueChangedEvent e => new ImmutableAggregate1(e.Value),
                _ => this
            };
        }
    }
}