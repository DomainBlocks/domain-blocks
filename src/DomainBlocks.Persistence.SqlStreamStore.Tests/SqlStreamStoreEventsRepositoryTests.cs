using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DomainBlocks.Core;
using DomainBlocks.Core.Serialization;
using DomainBlocks.Core.Serialization.SqlStreamStore;
using NUnit.Framework;
using SqlStreamStore;

namespace DomainBlocks.Persistence.SqlStreamStore.Tests;

[TestFixture]
public class SqlStreamStoreEventsRepositoryTests
{
    [Test]
    public async Task RoundTripTest()
    {
        using var streamStore = new InMemoryStreamStore();
        var eventNameMap = new EventNameMap();
        eventNameMap.Add(nameof(EventSaved), typeof(EventSaved));
        var serializer = new JsonStringEventDataSerializer();
        var eventAdapter = new SqlStreamStoreEventAdapter(serializer);
        var eventConverter = EventConverter.Create(eventNameMap, eventAdapter);

        const int readPageSize = 2;
        var eventsRepository = new SqlStreamStoreEventsRepository(streamStore, eventConverter, readPageSize);

        var events = Enumerable
            .Range(0, 10)
            .Select(i => new EventSaved($"Value {i}"))
            .ToList();

        var version = await eventsRepository.SaveEventsAsync("test-stream", -1, events);
        var loadedEvents = await eventsRepository.LoadEventsAsync("test-stream").ToListAsync();

        Assert.That(version, Is.EqualTo(events.Count - 1));
        Assert.That(loadedEvents, Is.EqualTo(events).Using(EventSavedEqualityComparer.Instance));
    }

    private class EventSaved
    {
        public EventSaved(string value)
        {
            Value = value;
        }

        public string Value { get; }
    }

    private sealed class EventSavedEqualityComparer : IEqualityComparer<EventSaved>
    {
        public static readonly IEqualityComparer<EventSaved> Instance = new EventSavedEqualityComparer();

        public bool Equals(EventSaved x, EventSaved y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(x, null)) return false;
            if (ReferenceEquals(y, null)) return false;
            if (x.GetType() != y.GetType()) return false;
            return x.Value == y.Value;
        }

        public int GetHashCode(EventSaved obj)
        {
            // Not used in tests.
            throw new NotImplementedException();
        }
    }
}