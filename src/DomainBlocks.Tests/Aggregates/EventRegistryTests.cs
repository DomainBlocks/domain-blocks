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
        var registry = EventRegistryBuilder.Create<object>()
            .For<MyAggregateRoot>()
            .Event<SomethingHappened>()
            .RoutesTo((a, e) => a.ApplyEvent(e))
            .HasName(EventNames.SomethingHappened)
            .Event<SomethingElseHappened>()
            .RoutesTo((a, e) => a.ApplyEvent(e))
            .HasName(EventNames.SomethingElseHappened)
            .Build();

        Assert.That(registry.EventNameMap.GetEventNameForClrType(typeof(SomethingHappened)),
            Is.EqualTo(EventNames.SomethingHappened));

        Assert.That(registry.EventNameMap.GetEventNameForClrType(typeof(SomethingElseHappened)),
            Is.EqualTo(EventNames.SomethingElseHappened));
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