using DomainBlocks.Aggregates.Builders;
using NUnit.Framework;

namespace DomainBlocks.Tests.Aggregates;

public static class EventNames
{
    public const string SomethingHappened = "SomethingHappened";
    public const string SomethingElseHappened = "SomethingElseHappened";
}

[TestFixture]
public class EventRegistryTests
{
    [Test]
    public void EventNamesAreMappedWhenEventsAreRegisteredWithName()
    {
        var registry = EventRegistryBuilder
            .OfType<object>()
            .For<MyAggregateRoot>(events =>
            {
                events.Event<SomethingHappened>()
                    .RoutesTo((a, e) => a.ApplyEvent(e))
                    .HasName(EventNames.SomethingHappened);

                events.Event<SomethingElseHappened>()
                    .RoutesTo((a, e) => a.ApplyEvent(e))
                    .HasName(EventNames.SomethingElseHappened);
            })
            .Build();

        Assert.That(registry.EventNameMap.GetEventName(typeof(SomethingHappened)),
            Is.EqualTo(EventNames.SomethingHappened));

        Assert.That(registry.EventNameMap.GetEventName(typeof(SomethingElseHappened)),
            Is.EqualTo(EventNames.SomethingElseHappened));

        Assert.That(registry.EventNameMap.GetEventType(EventNames.SomethingHappened),
            Is.EqualTo(typeof(SomethingHappened)));

        Assert.That(registry.EventNameMap.GetEventType(EventNames.SomethingElseHappened),
            Is.EqualTo(typeof(SomethingElseHappened)));
    }

    private class SomethingHappened
    {
    }

    private class SomethingElseHappened
    {
    }

    private class MyAggregateRoot
    {
        public MyAggregateRoot ApplyEvent(SomethingHappened @event)
        {
            return this;
        }

        public MyAggregateRoot ApplyEvent(SomethingElseHappened @event)
        {
            return this;
        }
    }
}