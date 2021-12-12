using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlStreamStore;

namespace DomainBlocks.Persistence.SqlStreamStore.AspNetCore
{
    public static class SqlStreamStoreServiceConfigurationExtensions
    {
        private static readonly object SyncLock = new();
        private static IStreamStore _sqlStreamStore;

        public static IServiceCollection AddPostgresSqlStreamStore(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<SqlStreamStoreConnectionOptions>()
                    .Bind(configuration.GetSection(SqlStreamStoreConnectionOptions.ConfigSection))
                    .ValidateDataAnnotations();

            services.AddSingleton(provider =>
            {
                if (_sqlStreamStore == null)
                {
                    lock (SyncLock)
                    {
                        if (_sqlStreamStore == null)
                        {
                            var options = provider.GetRequiredService<IOptions<SqlStreamStoreConnectionOptions>>();

                            var settings = new PostgresStreamStoreSettings(options.Value.ConnectionString);
                            _sqlStreamStore = new PostgresStreamStore(settings);

                            ((PostgresStreamStore)_sqlStreamStore).CreateSchemaIfNotExists().Wait();
                        }
                    }
                }

                return _sqlStreamStore;
            });

            return services;
        }
    }
}