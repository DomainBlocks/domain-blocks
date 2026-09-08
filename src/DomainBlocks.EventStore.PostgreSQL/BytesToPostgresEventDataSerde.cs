using System.Runtime.InteropServices;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore.PostgreSQL;

internal sealed class BytesToPostgresEventDataSerde(IObjectSerde<byte[]> serde) : IObjectSerde<PostgresEventData>
{
    public PostgresEventData Serialize(object value) => PostgresEventData.FromBytes(serde.Serialize(value));

    public object Deserialize(PostgresEventData value, Type type)
    {
        var bytes = value.Bytes;

        // Avoid a copy when the memory is a whole array.
        var array = MemoryMarshal.TryGetArray(bytes, out var segment) &&
                    segment.Offset == 0 &&
                    segment.Array is { } wholeArray &&
                    wholeArray.Length == segment.Count
            ? wholeArray
            : bytes.ToArray();

        return serde.Deserialize(array, type);
    }
}
