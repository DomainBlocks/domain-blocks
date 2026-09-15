using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

public static class NpgsqlDataSourceBuilderExtensions
{
    /// <summary>
    /// Maps the enums the event store's append function takes and returns, for the schema in
    /// <paramref name="options"/>. A data source given to <see cref="PostgresEventStore.Create{TEvent}"/> must have
    /// been built with this; call it once per schema when a data source serves more than one.
    /// </summary>
    /// <remarks>
    /// Npgsql resolves a mapping against the database's types when the data source opens its first connection. If the
    /// schema is created after that on the same data source,
    /// <see cref="PostgresEventStoreAdmin.EnsureInitializedAsync"/> reloads the types, so initializing and using the
    /// store over one data source works in either order.
    /// </remarks>
    public static NpgsqlDataSourceBuilder UsePostgresEventStore(
        this NpgsqlDataSourceBuilder builder,
        PostgresEventStoreOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var names = new SchemaObjectNames((options ?? new PostgresEventStoreOptions()).Schema);

        builder.MapEnum<AppendProtocol.ExpectedKind>(names.ExpectedStateKindType);
        builder.MapEnum<AppendProtocol.Status>(names.AppendStatusType);

        return builder;
    }
}