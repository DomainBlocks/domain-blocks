using NUnit.Framework;

namespace DomainBlocks.Core.Tests;

[TestFixture]
public class EventOptionsTests
{
    [Test]
    public void MergeMergesValuesCorrectly()
    {
        var eventOptions1 = new EventOptions<MyAggregate, IEvent, MyEvent>()
            .HideEventType();

        var eventOptions2 = new EventOptions<MyAggregate, IEvent, MyEvent>()
            .WithEventApplier((agg, e) => agg.Apply(e))
            .HideEventType();

        var eventOptions3 = new EventOptions<MyAggregate, IEvent, MyEvent>()
            .WithEventName("MyEvent2")
            .HideEventType();

        var result = eventOptions1
            .Merge(eventOptions2)
            .Merge(eventOptions3);

        var aggregate = new MyAggregate();
        result.ApplyEvent(aggregate, new MyEvent());

        Assert.That(result.ClrType, Is.EqualTo(typeof(MyEvent)));
        Assert.That(aggregate.IsEventApplied, Is.True);
        Assert.That(result.EventName, Is.EqualTo("MyEvent2"));
    }

    private class MyAggregate
    {
        public bool IsEventApplied { get; private set; }

        public void Apply(MyEvent _)
        {
            IsEventApplied = true;
        }
    }

    private interface IEvent
    {
    }

    private class MyEvent : IEvent
    {
    }
}