using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore.PostgreSQL;

public static class ObjectSerializerExtensions
{
    /// <summary>
    /// Adapts a JSON string serializer so that event data is stored in the <c>jsonb</c> column.
    /// </summary>
    public static IObjectSerializer<PostgresEventData> AsPostgresEventDataSerializer(
        this IObjectSerializer<string> serializer)
    {
        return new StringToPostgresEventDataSerializer(serializer);
    }

    /// <summary>
    /// Adapts a byte array serializer so that event data is stored in the <c>bytea</c> column.
    /// </summary>
    public static IObjectSerializer<PostgresEventData> AsPostgresEventDataSerializer(
        this IObjectSerializer<byte[]> serializer)
    {
        return new BytesToPostgresEventDataSerializer(serializer);
    }

    /// <summary>
    /// Adapts a byte memory serializer so that event data is stored in the <c>bytea</c> column.
    /// </summary>
    public static IObjectSerializer<PostgresEventData> AsPostgresEventDataSerializer(
        this IObjectSerializer<ReadOnlyMemory<byte>> serializer)
    {
        return new MemoryToPostgresEventDataSerializer(serializer);
    }
}