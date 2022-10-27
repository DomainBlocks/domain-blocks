using SqlStreamStore;

namespace DomainBlocks.Projections.SqlStreamStore.New;

public static class SqlStreamStoreOptionsBuilderExtensions
{
    // Having this as an extension method allows us to move it to a different assembly. Consider moving this method out
    // to a separate infrastructure specific assembly.
    public static void UsePostgres(
        this SqlStreamStoreOptionsBuilder optionsBuilder, PostgresStreamStoreSettings settings)
    {
        optionsBuilder.WithStreamStoreFactory(() =>
        {
            var streamStore = new PostgresStreamStore(settings);
            streamStore.CreateSchemaIfNotExists().Wait();
            return streamStore;
        });
    }
}