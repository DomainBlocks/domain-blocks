using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore.PostgreSQL;

internal sealed class StringToPostgresEventDataSerializer(IObjectSerializer<string> serializer) :
    IObjectSerializer<PostgresEventData>
{
    public PostgresEventData Serialize(object value) => PostgresEventData.FromJson(serializer.Serialize(value));

    public object Deserialize(PostgresEventData data, Type type) => serializer.Deserialize(data.Json, type);
}