using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore.PostgreSQL;

internal sealed class MemoryToPostgresEventDataSerde(IObjectSerde<ReadOnlyMemory<byte>> serde) :
    IObjectSerde<PostgresEventData>
{
    public PostgresEventData Serialize(object value) => PostgresEventData.FromBytes(serde.Serialize(value));

    public object Deserialize(PostgresEventData value, Type type) => serde.Deserialize(value.Bytes, type);
}
