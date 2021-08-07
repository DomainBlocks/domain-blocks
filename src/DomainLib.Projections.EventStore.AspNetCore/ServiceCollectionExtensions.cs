using System;
using DomainLib.EventStore.AspNetCore;
using EventStore.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DomainLib.Projections.EventStore.AspNetCore
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddEventStoreProjections(this IServiceCollection services,
                                                                  IConfiguration configuration)
        {
            {
                services.AddOptions<EventStoreConnectionOptions>()
                        .Bind(configuration.GetSection(EventStoreConnectionOptions.ConfigSection))
                        .ValidateDataAnnotations();

                services.AddSingleton(provider =>
                {
                    var options = provider.GetRequiredService<IOptions<EventStoreConnectionOptions>>();

                    var settings = EventStoreClientSettings.Create(options.Value.ConnectionString);
                    var client = new EventStoreClient(settings);

                    return client;
                });

                return services;
            }
        }
    }
}
