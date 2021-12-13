using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

            services.AddSingleton(_ =>
            {
                if (_sqlStreamStore == null)
                {
                    lock (SyncLock)
                    {
                        if (_sqlStreamStore == null)
                        {
                            _sqlStreamStore = new InMemoryStreamStore();
                        }
                    }
                }

                return _sqlStreamStore;
            });

            return services;
        }
    }
}