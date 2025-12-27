using System.Text.Json;
using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore.KurrentDb.Tests.Integration;

internal static class TestEventsHelper
{
    internal static UncommittedEvent<ReadOnlyMemory<byte>> CreateTestEvent(string eventName)
    {
        var header = new UncommittedEventHeader(eventName);

        var value = new Dictionary<string, string>
        {
            { "TestProperty", "TestValue" }
        };

        ReadOnlyMemory<byte> serializedValue = JsonSerializer.SerializeToUtf8Bytes(value);

        return UncommittedEvent.Create(header, serializedValue);
    }
}