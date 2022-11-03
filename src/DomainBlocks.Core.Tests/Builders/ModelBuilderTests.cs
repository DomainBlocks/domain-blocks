using System.Collections.Generic;
using System.Linq;
using DomainBlocks.Core.Builders;
using NUnit.Framework;

namespace DomainBlocks.Core.Tests.Builders;

[TestFixture]
public class ModelBuilderTests
{
    [Test]
    public void MutableAggregateScenario1()
    {
        var model = new ModelBuilder()
            .Aggregate<MutableAggregate1, object>(aggregate =>
            {
                aggregate.WithRaisedEventsFrom(x => x.RaisedEvents);
            })
            .Build();

        var aggregateOptions = model.GetAggregateOptions<MutableAggregate1>();

        var aggregate = new MutableAggregate1();
        var context = aggregateOptions.GetCommandExecutionContext(aggregate);

        context.ExecuteCommand(x => x.Execute("value 1"));
        context.ExecuteCommand(x => x.Execute("value 2"));
        var events = context.RaisedEvents.ToList();

        Assert.That(context.State, Is.SameAs(aggregate));
        Assert.That(context.State.Value, Is.EqualTo("value 2"));
        Assert.That(events, Has.Count.EqualTo(2));
        Assert.That(events[0], Is.TypeOf<ValueChangedEvent>());
        Assert.That(((ValueChangedEvent)events[0]).Value, Is.EqualTo("value 1"));
        Assert.That(events[1], Is.TypeOf<ValueChangedEvent>());
        Assert.That(((ValueChangedEvent)events[1]).Value, Is.EqualTo("value 2"));
    }

    [Test]
    public void MutableAggregateScenario2()
    {
        var model = new ModelBuilder()
            .Aggregate<MutableAggregate2, object>(aggregate =>
            {
                aggregate
                    .CommandResult<IEnumerable<object>>()
                    .WithEventsFrom(x => x)
                    .ApplyEventsWhileEnumerating();
                
                aggregate
                    .ApplyEventsByConvention()
                    .FromMethodName(nameof(MutableAggregate2.Apply))
                    .IncludeNonPublicMethods();
            })
            .Build();

        var aggregateOptions = model.GetAggregateOptions<MutableAggregate2>();

        var aggregate = new MutableAggregate2();
        var context = aggregateOptions.GetCommandExecutionContext(aggregate);

        context.ExecuteCommand(x => x.Execute("value"));
        var events = context.RaisedEvents.ToList();

        Assert.That(context.State, Is.SameAs(aggregate));
        Assert.That(context.State.ObservedValues, Has.Count.EqualTo(3));
        Assert.That(context.State.ObservedValues[0], Is.EqualTo("value 1"));
        Assert.That(context.State.ObservedValues[1], Is.EqualTo("value 1 2"));
        Assert.That(context.State.ObservedValues[2], Is.EqualTo("value 1 2 3"));
        Assert.That(events, Has.Count.EqualTo(3));
        Assert.That(events[0], Is.TypeOf<ValueChangedEvent>());
        Assert.That(events[1], Is.TypeOf<ValueChangedEvent>());
        Assert.That(events[2], Is.TypeOf<ValueChangedEvent>());
    }

    [Test]
    public void ImmutableAggregateScenario1()
    {
        var model = new ModelBuilder()
            .ImmutableAggregate<ImmutableAggregate1, object>(aggregate =>
            {
                aggregate
                    .CommandResult<IEnumerable<object>>()
                    .WithEventsFrom(x => x)
                    .ApplyEvents();

                aggregate
                    .ApplyEventsByConvention()
                    .FromMethodName(nameof(ImmutableAggregate1.Apply))
                    .IncludeNonPublicMethods();
            })
            .Build();

        var aggregateOptions = model.GetAggregateOptions<ImmutableAggregate1>();

        var aggregate = new ImmutableAggregate1();
        var context = aggregateOptions.GetCommandExecutionContext(aggregate);

        context.ExecuteCommand(x => x.Execute("value"));
        var events = context.RaisedEvents.ToList();

        Assert.That(context.State, Is.Not.SameAs(aggregate));
        Assert.That(context.State.Value, Is.EqualTo("value"));
        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0], Is.TypeOf<ValueChangedEvent>());
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
        public List<string> ObservedValues { get; } = new();
        private string Value { get; set; }

        public IEnumerable<object> Execute(string newValue)
        {
            yield return new ValueChangedEvent($"{newValue} 1");
            yield return new ValueChangedEvent($"{Value} 2");
            yield return new ValueChangedEvent($"{Value} 3");
        }

        public void Apply(ValueChangedEvent @event)
        {
            Value = @event.Value;
            ObservedValues.Add(@event.Value);
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

        public static ImmutableAggregate1 Apply(ValueChangedEvent @event)
        {
            return new ImmutableAggregate1(@event.Value);
        }
    }
}