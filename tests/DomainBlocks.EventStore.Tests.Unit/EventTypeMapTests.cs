using DomainBlocks.EventStore.TypeMapping;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit;

public class EventTypeMapTests
{
    [Test]
    public void MapType_WhenTypeMappedToName_MapsBothWays()
    {
        var map = EventTypeMap.Create(x => x.MapType<TestEvent1>(m => m.WithName("CustomName")));

        map.Appends.GetEventName(typeof(TestEvent1)).ShouldBe("CustomName");
        map.Reads.GetEventType("CustomName").ShouldBe(typeof(TestEvent1));
    }

    [Test]
    public void MapType_WhenNoNameProvided_UsesTypeName()
    {
        var map = EventTypeMap.Create(x => x.MapType<TestEvent2>());

        map.Appends.GetEventName(typeof(TestEvent2)).ShouldBe(nameof(TestEvent2));
        map.Reads.GetEventType(nameof(TestEvent2)).ShouldBe(typeof(TestEvent2));
    }

    [Test]
    public void MapType_WhenNameMappedToDifferentType_ThrowsException()
    {
        // Case 1: Name already mapped to a type via MapType
        {
            Should.Throw<EventTypeMapConfigurationException>(() =>
            {
                EventTypeMap.Create(builder => builder
                    .MapType<TestEvent1>(m => m.WithName("Name1"))
                    .MapType<TestEvent2>(m => m.WithName("Name1")));
            });
        }

        // Case 2: Name already mapped to a type via MapReadType
        {
            Should.Throw<EventTypeMapConfigurationException>(() =>
            {
                EventTypeMap.Create(builder => builder
                    .ForReads(reads => reads.MapType<TestEvent1>(m => m.FromNames("Name1")))
                    .MapType<TestEvent2>(m => m.WithName("Name1")));
            });
        }
    }

    [Test]
    public void MapReadType_WhenMultipleNamesProvided_MapsAllToType()
    {
        var map = EventTypeMap.Create(builder => builder
            .ForReads(reads => reads
                .MapType<TestEvent1>(m => m.FromNames("Name1", "Name2"))));

        map.Reads.GetEventType("Name1").ShouldBe(typeof(TestEvent1));
        map.Reads.GetEventType("Name2").ShouldBe(typeof(TestEvent1));
    }

    private class TestEvent1;

    private class TestEvent2;
}