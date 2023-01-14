using DomainBlocks.ThirdParty.SqlStreamStore.Postgres;

namespace DomainBlocks.SqlStreamStore.Postgres;

public static class SqlStreamStoreOptionsBuilderExtensions
{
    // Having this as an extension method allows us to move it to a different assembly. Consider moving this method out
    // to a separate infrastructure specific assembly.
    public static SqlStreamStoreOptionsBuilder UsePostgresStreamStore(
        this SqlStreamStoreOptionsBuilder optionsBuilder,
        PostgresStreamStoreSettings settings,
        bool createSchemaIfNotExists = false)
    {
        if (optionsBuilder == null) throw new ArgumentNullException(nameof(optionsBuilder));
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        optionsBuilder.WithStreamStoreFactory(() =>
        {
            var streamStore = new PostgresStreamStore(settings);

            if (createSchemaIfNotExists)
            {
                streamStore.CreateSchemaIfNotExists().Wait();
            }

            return streamStore;
        });

        return optionsBuilder;
    }

    public static SqlStreamStoreOptionsBuilder UsePostgresStreamStore(
        this SqlStreamStoreOptionsBuilder optionsBuilder,
        string connectionString,
        bool createSchemaIfNotExists = false)
    {
        var settings = new PostgresStreamStoreSettings(connectionString);
        return optionsBuilder.UsePostgresStreamStore(settings, createSchemaIfNotExists);
    }
}