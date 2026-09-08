using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore.PostgreSQL;

internal sealed class StringToPostgresEventDataSerde(IObjectSerde<string> serde) : IObjectSerde<PostgresEventData>
{
    public PostgresEventData Serialize(object value) => PostgresEventData.FromJson(serde.Serialize(value));

    public object Deserialize(PostgresEventData value, Type type) => serde.Deserialize(value.Json, type);
}
