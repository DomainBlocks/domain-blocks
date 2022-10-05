using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlStreamStore;

namespace DomainBlocks.SqlStreamStore.Common.AspNetCore;

public static class SqlStreamStoreServiceConfigurationExtensions
{
    private static readonly object SyncLock = new();
    private static PostgresStreamStore _sqlStreamStore;

    public static IServiceCollection AddPostgresSqlStreamStore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SqlStreamStoreConnectionOptions>()
            .Bind(configuration.GetSection(SqlStreamStoreConnectionOptions.ConfigSection))
            .ValidateDataAnnotations();

        services.AddSingleton<IStreamStore>(provider =>
        {
            if (_sqlStreamStore == null)
            {
                lock (SyncLock)
                {
                    if (_sqlStreamStore == null)
                    {
                        var options = provider.GetRequiredService<IOptions<SqlStreamStoreConnectionOptions>>();

                        var settings = new PostgresStreamStoreSettings(options.Value.ConnectionString);
                        if (!string.IsNullOrEmpty(options.Value.SchemaName))
                        {
                            settings.Schema = options.Value.SchemaName;
                        }

                        _sqlStreamStore = new PostgresStreamStore(settings);
                        _sqlStreamStore.CreateSchemaIfNotExists().Wait();
                        _sqlStreamStore.OnDispose += OnPostgresStreamStoreDisposed;
                    }
                }
            }
                
            return _sqlStreamStore;
        });

        return services;
    }

    private static void OnPostgresStreamStoreDisposed()
    {
        _sqlStreamStore = null;
    }
}