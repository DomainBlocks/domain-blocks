using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore.PostgreSQL;

internal sealed class BytesToPostgresEventDataSerde(IObjectSerde<byte[]> serde) : IObjectSerde<PostgresEventData>
{
    public PostgresEventData Serialize(object value) => PostgresEventData.FromBytes(serde.Serialize(value));

    public object Deserialize(PostgresEventData value, Type type)
    {
        return serde.Deserialize(value.Bytes.GetArrayOrCopy(), type);
    }
}