using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DomainLib.EventStore.AspNetCore;
using DomainLib.Projections;
using DomainLib.Projections.EventStore;
using EventStore.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DomainLib.Projections.AspNetCore
{
    public static class ReadModelServiceCollectionExtensions
    {
        public static IServiceCollection AddReadModel<TDbContext>(this IServiceCollection services,
                                                                  IConfiguration configuration,
                                                                  Action<ProjectionRegistryBuilder, TDbContext>
                                                                      onRegisteringProjections)
            where TDbContext : DbContext
        {
            return services.AddReadModel<object, TDbContext>(configuration, onRegisteringProjections);
        }

        public static IServiceCollection AddReadModel<TEventBase, TDbContext>(
            this IServiceCollection services,
            IConfiguration configuration,
            Action<ProjectionRegistryBuilder, TDbContext> onRegisteringProjections) where TDbContext : DbContext
        {
            services.AddHostedService(provider =>
            {
                var client = provider.GetRequiredService<EventStoreClient>();

                var dispatcherScope = provider.CreateScope();
                var dbContext = dispatcherScope.ServiceProvider.GetRequiredService<TDbContext>();
                var publisher = new EventStoreEventPublisher(client);
                var serializer = new JsonEventDeserializer();

                return new EventDispatcherHostedService<TEventBase>(new ProjectionRegistryBuilder(),
                                                                    publisher,
                                                                    x => onRegisteringProjections(x, dbContext));
            });

            return services;
        }
    }
}
