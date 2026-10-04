using DomainBlocks.EventStore.TypeMapping;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit;

public class EventTypeMapTests
{
    [Test]
    public void GetEventName_WhenTypeNotMapped_ThrowsWithType()
    {
        var map = new EventTypeMapBuilder().Add<TestEvent1>().Build();

        var exception = Should.Throw<EventTypeNotMappedException>(() => map.GetEventName(typeof(TestEvent2)));

        exception.EventType.ShouldBe(typeof(TestEvent2));
    }

    [Test]
    public void GetReadMapping_WhenNameNotMapped_ThrowsWithName()
    {
        var map = new EventTypeMapBuilder().Add<TestEvent1>().Build();

        var exception = Should.Throw<EventNameNotMappedException>(() => map.GetReadMapping("UnknownName"));

        exception.EventName.ShouldBe("UnknownName");
    }

    private class TestEvent1;

    private class TestEvent2;
}