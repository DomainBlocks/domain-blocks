using System.Text.Json;
using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Integration;

internal static class TestEventsHelper
{
    internal static SerializedAppendEvent<ReadOnlyMemory<byte>> CreateTestEvent(string eventName)
    {
        var value = new Dictionary<string, string>
        {
            { "TestProperty", "TestValue" }
        };

        ReadOnlyMemory<byte> serializedValue = JsonSerializer.SerializeToUtf8Bytes(value);

        return SerializedAppendEvent.Create(eventName, serializedValue);
    }
}