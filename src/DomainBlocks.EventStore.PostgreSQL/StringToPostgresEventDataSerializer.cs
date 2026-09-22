using System.Reflection;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore.PostgreSQL;

internal sealed class StringToPostgresEventDataSerializer(IObjectSerializer<string> serializer) :
    IObjectSerializer<PostgresEventData>
{
    public PostgresEventData Serialize(object value) => PostgresEventData.FromJson(serializer.Serialize(value));

    public object Deserialize(PostgresEventData data, Type type) => serializer.Deserialize(data.Json, type);

    // The text is stored as jsonb, which can be looked into, under the names it was written with.
    public string? GetStoredName(Type type, MemberInfo member) => serializer.GetStoredName(type, member);
}