using System;
using DomainLib.Aggregates.Registration;
using DomainLib.Persistence;
using DomainLib.Persistence.EventStore;
using DomainLib.Serialization.Json;
using EventStore.ClientAPI;
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
        public static void AddEventStoreAggregateRepository(this IServiceCollection services,
                                                            IConfiguration configuration,
                                                            AggregateRegistry<object, object> aggregateRegistry)
        {
            AddEventStoreAggregateRepository<object, object>(services, configuration, aggregateRegistry);
        }

        /// <summary>
        /// Adds an aggregate repository that uses EventStoreDb to persist events 
        /// </summary>
        public static void AddEventStoreAggregateRepository<TCommandBase, TEventBase>(this IServiceCollection services,
            IConfiguration configuration,
            AggregateRegistry<TCommandBase, TEventBase> aggregateRegistry)
        {
            services.AddOptions<EventStoreConnectionOptions>()
                    .Bind(configuration.GetSection(EventStoreConnectionOptions.ConfigSection))
                    .ValidateDataAnnotations();

            services.AddSingleton(provider =>
            {
                var options = provider.GetRequiredService<IOptions<EventStoreConnectionOptions>>();

                var connectionSettings = ConnectionSettings.Create().DisableTls().Build();
                var connection = EventStoreConnection.Create(connectionSettings, options.Value.Uri);

                return connection;
            });

            services.AddHostedService(provider =>
            {
                var connection = provider.GetRequiredService<IEventStoreConnection>();
                return new EventStoreConnectionHostedService(connection);
            });

            // TODO: It would be nice to be able to customize the serializer through the service registration
            var serializer = new JsonBytesEventSerializer(aggregateRegistry.EventNameMap);

            services.AddSingleton<IAggregateRepository<TEventBase>>(provider =>
            {
                var connection = provider.GetRequiredService<IEventStoreConnection>();

                var eventsRepository = new EventStoreEventsRepository(connection, serializer);
                var snapshotRepository = new EventStoreSnapshotRepository(connection, serializer);

                return AggregateRepository.Create(eventsRepository,
                                                  snapshotRepository,
                                                  aggregateRegistry.EventDispatcher,
                                                  aggregateRegistry.AggregateMetadataMap);
            });

            services.AddSingleton(aggregateRegistry.CommandDispatcher);
        }
    }
}
