using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

public static class NpgsqlDataSourceBuilderExtensions
{
    /// <summary>
    /// Maps the event store's enum types for the schema in <paramref name="options"/>.
    /// </summary>
    /// <remarks>
    /// A data source passed to <see cref="PostgresEventStoreBuilder{TEvent}.UseDataSource"/> must be built with this
    /// mapping.
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