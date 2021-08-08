using System;
using DomainLib.Aggregates.Registration;
using DomainLib.Persistence;
using DomainLib.Persistence.EventStore;
using DomainLib.Projections;
using DomainLib.Projections.EventStore;
using DomainLib.Serialization.Json;
using EventStore.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DomainLib.EventStore.AspNetCore
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds an aggregate repository that uses EventStoreDb to persist events 
        /// </summary>
        public static IServiceCollection AddEventStoreAggregateRepository(this IServiceCollection services,
                                                                          IConfiguration configuration,
                                                                          AggregateRegistry<object, object>
                                                                              aggregateRegistry)
        {
            return AddEventStoreAggregateRepository<object, object>(services, configuration, aggregateRegistry);
        }

        /// <summary>
        /// Adds an aggregate repository that uses EventStoreDb to persist events 
        /// </summary>
        public static IServiceCollection AddEventStoreAggregateRepository<TCommandBase, TEventBase>(
            this IServiceCollection services,
            IConfiguration configuration,
            AggregateRegistry<TCommandBase, TEventBase> aggregateRegistry)
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

            // TODO: It would be nice to be able to customize the serializer through the service registration
            var serializer = new JsonBytesEventSerializer(aggregateRegistry.EventNameMap);

            services.AddSingleton<IAggregateRepository<TEventBase>>(provider =>
            {
                var connection = provider.GetRequiredService<EventStoreClient>();

                var eventsRepository = new EventStoreEventsRepository(connection, serializer);
                var snapshotRepository = new EventStoreSnapshotRepository(connection, serializer);

                return AggregateRepository.Create(eventsRepository,
                                                  snapshotRepository,
                                                  aggregateRegistry.EventDispatcher,
                                                  aggregateRegistry.AggregateMetadataMap);
            });

            services.AddSingleton(aggregateRegistry.CommandDispatcher);

            return services;
        }
    }

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

            services.AddHostedService(provider =>
            {
                var client = provider.GetRequiredService<EventStoreClient>();

                var dispatcherScope = provider.CreateScope();
                var dbContext = dispatcherScope.ServiceProvider.GetRequiredService<TDbContext>();
                var publisher = new EventStoreEventPublisher(client);

                return new EventDispatcherHostedService<TEventBase>(new ProjectionRegistryBuilder(),
                                                                    publisher,
                                                                    x => onRegisteringProjections(x, dbContext));
            });

            return services;
        }
    }
}
