using DomainBlocks.EventStore.Benchmarks.Proto;

namespace DomainBlocks.EventStore.Benchmarks;

internal interface IDomainEvent;

internal sealed class TestEvent : IDomainEvent
{
    public required string Value1 { get; init; }
    public required string Value2 { get; init; }
}

/// <summary>
/// Same shape as <see cref="TestEvent"/>, stored under another name, for reads that select by event name.
/// </summary>
internal sealed class OtherTestEvent : IDomainEvent
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
    /// <paramref name="testEventPercent"/> in every hundred are a <see cref="TestEvent"/> and the rest an
    /// <see cref="OtherTestEvent"/>. Protobuf formats have one type only.
    /// </summary>
    public static AppendableEvent<IDomainEvent>[] Create(
        SerializationFormat format,
        int count,
        int metadataEntryCount,
        int testEventPercent = 100)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(metadataEntryCount, 2);

        if (format.IsProtobuf)
            ArgumentOutOfRangeException.ThrowIfNotEqual(testEventPercent, 100);

        var events = new AppendableEvent<IDomainEvent>[count];

        for (var i = 0; i < count; i++)
        {
            var value1 = $"value1-{i}";
            var value2 = $"value2-{i}";

            IDomainEvent @event = format.IsProtobuf
                ? new ProtoTestEvent { Value1 = value1, Value2 = value2 }
                : i % 100 < testEventPercent
                    ? new TestEvent { Value1 = value1, Value2 = value2 }
                    : new OtherTestEvent { Value1 = value1, Value2 = value2 };

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
