using DomainBlocks.EventStore.Benchmarks.Proto;

namespace DomainBlocks.EventStore.Benchmarks;

internal interface IDomainEvent;

internal sealed class TestEvent : IDomainEvent
{
    public required string Value1 { get; init; }
    public required string Value2 { get; init; }
}

internal static class TestEvents
{
    /// <summary>
    /// Creates <paramref name="count"/> events of the type that <paramref name="format"/> serializes. Every event has
    /// the same two string values whatever its type, and carries the first <paramref name="metadataEntryCount"/> of
    /// them again as metadata, so that with two entries the payload and metadata are of equivalent size.
    /// </summary>
    public static AppendableEvent<IDomainEvent>[] Create(
        SerializationFormat format,
        int count,
        int metadataEntryCount)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(metadataEntryCount, 2);


        var events = new AppendableEvent<IDomainEvent>[count];

        for (var i = 0; i < count; i++)
        {
            var value1 = $"value1-{i}";
            var value2 = $"value2-{i}";

            IDomainEvent @event = format.IsProtobuf
                ? new ProtoTestEvent { Value1 = value1, Value2 = value2 }
                : new TestEvent { Value1 = value1, Value2 = value2 };

            KeyValuePair<string, string>[] metadata =
            [
                KeyValuePair.Create("Value1", value1),
                KeyValuePair.Create("Value2", value2)
            ];

            events[i] = new AppendableEvent<IDomainEvent>(@event, metadata[..metadataEntryCount]);
        }

        return events;
    }

    extension(SerializationFormat format)
    {
        public bool IsProtobuf => format is SerializationFormat.Protobuf or SerializationFormat.ProtobufJson;
    }
}
