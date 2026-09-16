using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore.PostgreSQL;

internal sealed class BytesToPostgresEventDataSerializer(IObjectSerializer<byte[]> serializer) :
    IObjectSerializer<PostgresEventData>
{
    public PostgresEventData Serialize(object value) => PostgresEventData.FromBytes(serializer.Serialize(value));

    public object Deserialize(PostgresEventData data, Type type)
    {
        return serializer.Deserialize(data.Bytes.GetArrayOrCopy(), type);
    }
}