using DomainBlocks.Core.Exceptions;
using DomainBlocks.EventStore.TypeMapping;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit;

public class EventTypeMapTests
{
    [Test]
    public void ReadWrite_WhenNoNameProvided_RoundTripsUnderTypeName()
    {
        var map = EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent1>());

        map.GetEventName(typeof(TestEvent1)).ShouldBe(nameof(TestEvent1));
        map.GetEventType(nameof(TestEvent1)).ShouldBe(typeof(TestEvent1));
    }

    [Test]
    public void ReadWrite_WhenNameProvided_RoundTripsUnderThatNameOnly()
    {
        var map = EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent1>("CustomName"));

        map.GetEventName(typeof(TestEvent1)).ShouldBe("CustomName");
        map.GetEventType("CustomName").ShouldBe(typeof(TestEvent1));

        Should.Throw<EventNameNotMappedException>(() => map.GetEventType(nameof(TestEvent1)));
    }

    [Test]
    public void WriteOnly_MapsTypeToNameButNotNameToType()
    {
        var map = EventTypeMap.Create(EventTypeMapping.WriteOnly<TestEvent1>());

        map.GetEventName(typeof(TestEvent1)).ShouldBe(nameof(TestEvent1));

        Should.Throw<EventNameNotMappedException>(() => map.GetEventType(nameof(TestEvent1)));
    }

    [Test]
    public void ReadOnly_MapsNamesToTypeButNotTypeToName()
    {
        var map = EventTypeMap.Create(EventTypeMapping.ReadOnly<TestEvent1>("Name1", "Name2"));

        map.GetEventType("Name1").ShouldBe(typeof(TestEvent1));
        map.GetEventType("Name2").ShouldBe(typeof(TestEvent1));

        Should.Throw<EventTypeNotMappedException>(() => map.GetEventName(typeof(TestEvent1)));
    }

    [Test]
    public void ReadOnly_WhenNoNamesProvided_UsesTypeName()
    {
        var map = EventTypeMap.Create(EventTypeMapping.ReadOnly<TestEvent1>());

        map.GetEventType(nameof(TestEvent1)).ShouldBe(typeof(TestEvent1));
    }

    [Test]
    public void Create_WhenEventRenamed_ReadsOldAndNewNamesAsSameType()
    {
        var map = EventTypeMap.Create(
            EventTypeMapping.ReadWrite<TestEvent1>("NewName"),
            EventTypeMapping.ReadOnly<TestEvent1>("OldName"));

        map.GetEventName(typeof(TestEvent1)).ShouldBe("NewName");
        map.GetEventType("NewName").ShouldBe(typeof(TestEvent1));
        map.GetEventType("OldName").ShouldBe(typeof(TestEvent1));
    }

    [Test]
    public void Create_WhenNamesShareCommonReadType_ReadsAllNamesAsCommonType()
    {
        var map = EventTypeMap.Create(
            EventTypeMapping.WriteOnly<TestEvent1>(),
            EventTypeMapping.WriteOnly<TestEvent2>(),
            EventTypeMapping.ReadOnly<CommonTestEvent>(nameof(TestEvent1), nameof(TestEvent2)));

        map.GetEventName(typeof(TestEvent1)).ShouldBe(nameof(TestEvent1));
        map.GetEventName(typeof(TestEvent2)).ShouldBe(nameof(TestEvent2));
        map.GetEventType(nameof(TestEvent1)).ShouldBe(typeof(CommonTestEvent));
        map.GetEventType(nameof(TestEvent2)).ShouldBe(typeof(CommonTestEvent));
    }

    [Test]
    public void Create_WhenTypeWrittenAsTwoNames_ThrowsException()
    {
        var exception = Should.Throw<DomainBlocksException>(() => EventTypeMap.Create(
            EventTypeMapping.ReadWrite<TestEvent1>(),
            EventTypeMapping.WriteOnly<TestEvent1>("OtherName")));

        exception.Message.ShouldContain("type 'TestEvent1' is already mapped to name 'TestEvent1'");
    }

    [Test]
    public void Create_WhenNameReadAsTwoTypes_ThrowsException()
    {
        var exception = Should.Throw<DomainBlocksException>(() => EventTypeMap.Create(
            EventTypeMapping.ReadWrite<TestEvent1>(),
            EventTypeMapping.ReadOnly<CommonTestEvent>(nameof(TestEvent1))));

        exception.Message.ShouldContain("name 'TestEvent1' is already mapped to type 'TestEvent1'");
    }

    [Test]
    public void Create_WhenNameReadTwiceForSameType_ThrowsException()
    {
        Should.Throw<DomainBlocksException>(() => EventTypeMap.Create(
            EventTypeMapping.ReadOnly<TestEvent1>("Name1"),
            EventTypeMapping.ReadOnly<TestEvent1>("Name1")));
    }

    [Test]
    public void GetEventName_WhenTypeNotMapped_ThrowsWithType()
    {
        var map = EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent1>());

        var exception = Should.Throw<EventTypeNotMappedException>(() => map.GetEventName(typeof(TestEvent2)));

        exception.EventType.ShouldBe(typeof(TestEvent2));
    }

    [Test]
    public void GetEventType_WhenNameNotMapped_ThrowsWithName()
    {
        var map = EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent1>());

        var exception = Should.Throw<EventNameNotMappedException>(() => map.GetEventType("UnknownName"));

        exception.EventName.ShouldBe("UnknownName");
    }

    [Test]
    public void Factories_WithTypeArgument_BehaveLikeGenericForms()
    {
        var map = EventTypeMap.Create(
            EventTypeMapping.ReadWrite(typeof(TestEvent1)),
            EventTypeMapping.WriteOnly(typeof(TestEvent2), "Name2"),
            EventTypeMapping.ReadOnly(typeof(CommonTestEvent), "Name3"));

        map.GetEventName(typeof(TestEvent1)).ShouldBe(nameof(TestEvent1));
        map.GetEventType(nameof(TestEvent1)).ShouldBe(typeof(TestEvent1));
        map.GetEventName(typeof(TestEvent2)).ShouldBe("Name2");
        map.GetEventType("Name3").ShouldBe(typeof(CommonTestEvent));
    }

    [Test]
    public void Factories_WhenNameEmptyOrWhitespace_ThrowException()
    {
        Should.Throw<ArgumentException>(() => EventTypeMapping.ReadWrite<TestEvent1>(""));
        Should.Throw<ArgumentException>(() => EventTypeMapping.WriteOnly<TestEvent1>(" "));
        Should.Throw<ArgumentException>(() => EventTypeMapping.ReadOnly<TestEvent1>("Name1", ""));
    }

    private class TestEvent1;

    private class TestEvent2;

    private class CommonTestEvent;
}