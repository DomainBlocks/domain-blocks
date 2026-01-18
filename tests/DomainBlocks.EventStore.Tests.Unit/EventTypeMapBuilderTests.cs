using DomainBlocks.EventStore.TypeMapping;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit;

public class EventTypeMapBuilderTests
{
    [Test]
    public void MapType_WhenTypeMappedToName_MapsBothWays()
    {
        var map = new EventTypeMapBuilder()
            .MapType<TestEvent1>("CustomName")
            .Build();

        map.Append.GetEventName(typeof(TestEvent1)).ShouldBe("CustomName");
        map.Read.GetEventType("CustomName").ShouldBe(typeof(TestEvent1));
    }

    [Test]
    public void MapType_WhenNoNameProvided_UsesTypeName()
    {
        var map = new EventTypeMapBuilder()
            .MapType<TestEvent2>()
            .Build();

        map.Append.GetEventName(typeof(TestEvent2)).ShouldBe(nameof(TestEvent2));
        map.Read.GetEventType(nameof(TestEvent2)).ShouldBe(typeof(TestEvent2));
    }

    [Test]
    public void MapType_WhenTypeMappedToDifferentName_ThrowsException()
    {
        var builder = new EventTypeMapBuilder()
            .MapType<TestEvent1>("Name1");

        Should.Throw<EventTypeMapConfigurationException>(() => builder.MapType<TestEvent1>("Name2"));
    }

    [Test]
    public void MapType_WhenNameMappedToDifferentType_ThrowsException()
    {
        // Case 1: Name already mapped to a type via MapType
        {
            var builder = new EventTypeMapBuilder()
                .MapType<TestEvent1>("Name1");

            Should.Throw<EventTypeMapConfigurationException>(() => builder.MapType<TestEvent2>("Name1"));
        }

        // Case 2: Name already mapped to a type via MapReadType
        {
            var builder = new EventTypeMapBuilder()
                .Read.MapType<TestEvent1>("Name1");

            Should.Throw<EventTypeMapConfigurationException>(() => builder.MapType<TestEvent2>("Name1"));
        }
    }

    [Test]
    public void MapReadType_WhenMultipleNamesProvided_MapsAllToType()
    {
        var map = new EventTypeMapBuilder()
            .Read.MapType<TestEvent1>("Name1", "Name2")
            .Build();

        map.Read.GetEventType("Name1").ShouldBe(typeof(TestEvent1));
        map.Read.GetEventType("Name2").ShouldBe(typeof(TestEvent1));
    }

    [Test]
    public void MapReadType_WhenNameMappedToDifferentType_ThrowsException()
    {
        var builder = new EventTypeMapBuilder()
            .Read.MapType<TestEvent1>("Name1");

        Should.Throw<EventTypeMapConfigurationException>(() => builder.Read.MapType<TestEvent2>("Name1"));
    }

    private class TestEvent1;

    private class TestEvent2;
}