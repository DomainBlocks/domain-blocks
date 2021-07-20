using System;
using System.Threading;
using System.Threading.Tasks;
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
using Microsoft.Extensions.Hosting;
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

                return new EventDispatcherHostedService<TEventBase, TDbContext>(new ProjectionRegistryBuilder(),
                                                                                    publisher,
                                                                                    dbContext,
                                                                                    onRegisteringProjections);
            });

            return services;
        }
    }

    public class EventDispatcherHostedService<TEventBase, TDbContext> : IHostedService where TDbContext : DbContext
    {
        private readonly ProjectionRegistryBuilder _registryBuilder;
        private readonly EventStoreEventPublisher _publisher;
        private readonly TDbContext _dbContext;
        private readonly Action<ProjectionRegistryBuilder, TDbContext> _onRegisteringProjections;

        public EventDispatcherHostedService(ProjectionRegistryBuilder registryBuilder,
                                            EventStoreEventPublisher publisher,
                                            TDbContext dbContext,
                                            Action<ProjectionRegistryBuilder, TDbContext> onRegisteringProjections)
        {
            _registryBuilder = registryBuilder;
            _publisher = publisher;
            _dbContext = dbContext;
            _onRegisteringProjections = onRegisteringProjections;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _onRegisteringProjections(_registryBuilder, _dbContext);
            var projectionRegistry = _registryBuilder.Build();

            var dispatcher = new EventDispatcher<TEventBase>(_publisher,
                                                             projectionRegistry.EventProjectionMap,
                                                             projectionRegistry.EventContextMap,
                                                             new JsonEventDeserializer(),
                                                             projectionRegistry.EventNameMap,
                                                             EventDispatcherConfiguration.ReadModelDefaults);

            await dispatcher.StartAsync();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
