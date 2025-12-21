using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.KurrentDb.Tests.Integration;

internal static class TestEventsHelper
{
    internal static UncommittedEvent<ReadOnlyMemory<byte>> CreateTestEvent(string eventName)
    {
        var payload = new Dictionary<string, string>
        {
            { "TestProperty", "TestValue" }
        };

        var header = new UncommittedEventHeader(eventName);
        ReadOnlyMemory<byte> serializeToUtf8Bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload);

        return UncommittedEvent.Create(header, serializeToUtf8Bytes);
    }
}