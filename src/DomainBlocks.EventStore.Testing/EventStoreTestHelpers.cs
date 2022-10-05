using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.EventStore.Testing;

public static class EventStoreTestHelpers
{
    public static string RandomStreamName()
    {
        return $"someStream-{Guid.NewGuid()}";
    }

    public static IEnumerable<TestEvent> GenerateTestEvents(int number)
    {
        return Enumerable.Range(1, number).Select(n => new TestEvent(n));
    }
}