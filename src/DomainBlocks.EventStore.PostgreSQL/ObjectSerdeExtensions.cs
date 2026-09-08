using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore.PostgreSQL;

public static class ObjectSerdeExtensions
{
    /// <summary>
    /// Adapts a JSON string serde so that event data is stored in the <c>jsonb</c> column.
    /// </summary>
    public static IObjectSerde<PostgresEventData> AsPostgresEventDataSerde(this IObjectSerde<string> serde)
    {
        return new StringToPostgresEventDataSerde(serde);
    }

    /// <summary>
    /// Adapts a byte array serde so that event data is stored in the <c>bytea</c> column.
    /// </summary>
    public static IObjectSerde<PostgresEventData> AsPostgresEventDataSerde(this IObjectSerde<byte[]> serde)
    {
        return new BytesToPostgresEventDataSerde(serde);
    }

    /// <summary>
    /// Adapts a byte memory serde so that event data is stored in the <c>bytea</c> column.
    /// </summary>
    public static IObjectSerde<PostgresEventData> AsPostgresEventDataSerde(
        this IObjectSerde<ReadOnlyMemory<byte>> serde)
    {
        return new MemoryToPostgresEventDataSerde(serde);
    }
}
