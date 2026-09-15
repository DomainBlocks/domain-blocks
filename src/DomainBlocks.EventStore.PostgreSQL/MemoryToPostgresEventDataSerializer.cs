using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore.PostgreSQL;

internal sealed class MemoryToPostgresEventDataSerializer(IObjectSerializer<ReadOnlyMemory<byte>> serializer) :
    IObjectSerializer<PostgresEventData>
{
    public PostgresEventData Serialize(object value) => PostgresEventData.FromBytes(serializer.Serialize(value));

    public object Deserialize(PostgresEventData data, Type type) => serializer.Deserialize(data.Bytes, type);
}