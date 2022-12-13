using System;
using SqlStreamStore;

namespace DomainBlocks.Persistence.SqlStreamStore;

public static class SqlStreamStoreOptionsBuilderExtensions
{
    // Having this as an extension method allows us to move it to a different assembly. Consider moving this method out
    // to a separate infrastructure specific assembly.
    public static SqlStreamStoreOptionsBuilder UsePostgres(
        this SqlStreamStoreOptionsBuilder optionsBuilder, PostgresStreamStoreSettings settings)
    {
        if (optionsBuilder == null) throw new ArgumentNullException(nameof(optionsBuilder));
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        
        optionsBuilder.WithStreamStoreFactory(() =>
        {
            var streamStore = new PostgresStreamStore(settings);
            streamStore.CreateSchemaIfNotExists().Wait();
            return streamStore;
        });

        return optionsBuilder;
    }
}