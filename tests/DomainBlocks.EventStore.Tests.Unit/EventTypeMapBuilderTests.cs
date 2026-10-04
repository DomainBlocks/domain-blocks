using DomainBlocks.Core.Exceptions;
using DomainBlocks.EventStore.TypeMapping;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit;

public class EventTypeMapBuilderTests
{
    [Test]
    public void Add_WhenNoNameProvided_RoundTripsUnderTypeName()
    {
        var map = new EventTypeMapBuilder().Add<TestEvent1>().Build();

        map.GetEventName(typeof(TestEvent1)).ShouldBe(nameof(TestEvent1));

        map.GetReadMapping(nameof(TestEvent1))
            .ShouldBeOfType<EventTypeMapping.ReadToType>()
            .EventType
            .ShouldBe(typeof(TestEvent1));
    }

    [Test]
    public void Add_WhenNameProvided_RoundTripsUnderThatNameOnly()
    {
        var map = new EventTypeMapBuilder().Add<TestEvent1>("CustomName").Build();

        map.GetEventName(typeof(TestEvent1)).ShouldBe("CustomName");

        map.GetReadMapping("CustomName")
            .ShouldBeOfType<EventTypeMapping.ReadToType>()
            .EventType
            .ShouldBe(typeof(TestEvent1));

        Should.Throw<EventNameNotMappedException>(() => map.GetReadMapping(nameof(TestEvent1)));
    }

    [Test]
    public void Add_WithTypeArgument_BehavesLikeGenericForm()
    {
        var map = new EventTypeMapBuilder().Add(typeof(TestEvent1)).Build();

        map.GetEventName(typeof(TestEvent1)).ShouldBe(nameof(TestEvent1));

        map.GetReadMapping(nameof(TestEvent1))
            .ShouldBeOfType<EventTypeMapping.ReadToType>()
            .EventType
            .ShouldBe(typeof(TestEvent1));
    }

    [Test]
    public void Add_WhenNameEmptyOrWhitespace_ThrowsException()
    {
        Should.Throw<ArgumentException>(() => new EventTypeMapBuilder().Add<TestEvent1>(string.Empty));
        Should.Throw<ArgumentException>(() => new EventTypeMapBuilder().Add<TestEvent1>(" "));
    }

    [Test]
    public void AddWrite_WhenNoNameProvided_MapsTypeToNameButNotNameToType()
    {
        var map = new EventTypeMapBuilder().AddWrite<TestEvent1>().Build();

        map.GetEventName(typeof(TestEvent1)).ShouldBe(nameof(TestEvent1));

        Should.Throw<EventNameNotMappedException>(() => map.GetReadMapping(nameof(TestEvent1)));
    }

    [Test]
    public void AddWrite_WithTypeArgument_BehavesLikeGenericForm()
    {
        var map = new EventTypeMapBuilder().AddWrite(typeof(TestEvent1), "Name1").Build();

        map.GetEventName(typeof(TestEvent1)).ShouldBe("Name1");

        Should.Throw<EventNameNotMappedException>(() => map.GetReadMapping("Name1"));
    }

    [Test]
    public void AddWrite_WhenNameEmptyOrWhitespace_ThrowsException()
    {
        Should.Throw<ArgumentException>(() => new EventTypeMapBuilder().AddWrite<TestEvent1>(string.Empty));
        Should.Throw<ArgumentException>(() => new EventTypeMapBuilder().AddWrite<TestEvent1>(" "));
    }

    [Test]
    public void AddRead_WhenNamesProvided_MapsNamesToTypeButNotTypeToName()
    {
        var map = new EventTypeMapBuilder().AddRead<TestEvent1>("Name1", "Name2").Build();

        map.GetReadMapping("Name1")
            .ShouldBeOfType<EventTypeMapping.ReadToType>()
            .EventType
            .ShouldBe(typeof(TestEvent1));

        map.GetReadMapping("Name2")
            .ShouldBeOfType<EventTypeMapping.ReadToType>()
            .EventType
            .ShouldBe(typeof(TestEvent1));

        Should.Throw<EventTypeNotMappedException>(() => map.GetEventName(typeof(TestEvent1)));
    }

    [Test]
    public void AddRead_WhenNoNamesProvided_UsesTypeName()
    {
        var map = new EventTypeMapBuilder().AddRead<TestEvent1>().Build();

        map.GetReadMapping(nameof(TestEvent1))
            .ShouldBeOfType<EventTypeMapping.ReadToType>()
            .EventType
            .ShouldBe(typeof(TestEvent1));
    }

    [Test]
    public void AddRead_WithTypeArgument_BehavesLikeGenericForm()
    {
        var map = new EventTypeMapBuilder().AddRead(typeof(TestEvent1), "Name1").Build();

        map.GetReadMapping("Name1")
            .ShouldBeOfType<EventTypeMapping.ReadToType>()
            .EventType
            .ShouldBe(typeof(TestEvent1));

        Should.Throw<EventTypeNotMappedException>(() => map.GetEventName(typeof(TestEvent1)));
    }

    [Test]
    public void AddRead_WhenAnyNameEmptyOrWhitespace_ThrowsException()
    {
        Should.Throw<ArgumentException>(() => new EventTypeMapBuilder().AddRead<TestEvent1>("Name1", string.Empty));
        Should.Throw<ArgumentException>(() => new EventTypeMapBuilder().AddRead<TestEvent1>("Name1", " "));
    }

    [Test]
    public void Build_WhenEventRenamed_ReadsOldAndNewNamesAsSameType()
    {
        var map = new EventTypeMapBuilder()
            .Add<TestEvent1>("NewName")
            .AddRead<TestEvent1>("OldName")
            .Build();

        map.GetEventName(typeof(TestEvent1)).ShouldBe("NewName");

        map.GetReadMapping("NewName")
            .ShouldBeOfType<EventTypeMapping.ReadToType>()
            .EventType
            .ShouldBe(typeof(TestEvent1));

        map.GetReadMapping("OldName")
            .ShouldBeOfType<EventTypeMapping.ReadToType>()
            .EventType
            .ShouldBe(typeof(TestEvent1));
    }

    [Test]
    public void Build_WhenNamesShareCommonReadType_ReadsAllNamesAsCommonType()
    {
        var map = new EventTypeMapBuilder()
            .AddWrite<TestEvent1>()
            .AddWrite<TestEvent2>()
            .AddRead<CommonTestEvent>(nameof(TestEvent1), nameof(TestEvent2))
            .Build();

        map.GetEventName(typeof(TestEvent1)).ShouldBe(nameof(TestEvent1));
        map.GetEventName(typeof(TestEvent2)).ShouldBe(nameof(TestEvent2));

        map.GetReadMapping(nameof(TestEvent1))
            .ShouldBeOfType<EventTypeMapping.ReadToType>()
            .EventType
            .ShouldBe(typeof(CommonTestEvent));

        map.GetReadMapping(nameof(TestEvent2))
            .ShouldBeOfType<EventTypeMapping.ReadToType>()
            .EventType
            .ShouldBe(typeof(CommonTestEvent));
    }

    [Test]
    public void Build_WhenTypeWrittenAsTwoNames_ThrowsException()
    {
        var exception = Should.Throw<DomainBlocksException>(() => new EventTypeMapBuilder()
            .AddWrite<TestEvent1>()
            .AddWrite<TestEvent1>("OtherName")
            .Build());

        exception.Message.ShouldContain("Cannot add [Write typeof(TestEvent1) -> 'OtherName']");
    }

    [Test]
    public void Build_WhenNameReadAsTwoTypes_ThrowsException()
    {
        var exception = Should.Throw<DomainBlocksException>(() => new EventTypeMapBuilder()
            .Add<TestEvent1>()
            .AddRead<CommonTestEvent>(nameof(TestEvent1))
            .Build());

        exception.Message.ShouldContain("Cannot add [Read 'TestEvent1' -> typeof(CommonTestEvent)]");
    }

    [Test]
    public void Build_WhenNameReadTwiceForSameType_ThrowsException()
    {
        Should.Throw<DomainBlocksException>(() => new EventTypeMapBuilder()
            .Add<TestEvent1>("Name1")
            .Add<TestEvent1>("Name1")
            .Build());
    }

    private class TestEvent1;

    private class TestEvent2;

    private class CommonTestEvent;
}
