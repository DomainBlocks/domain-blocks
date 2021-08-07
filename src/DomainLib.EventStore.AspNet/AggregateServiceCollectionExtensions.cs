using System;
using DomainLib.Aggregates.Registration;
using DomainLib.Persistence;
using DomainLib.Persistence.EventStore;
using DomainLib.Serialization;
using DomainLib.Serialization.Json;
using EventStore.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DomainLib.EventStore.AspNetCore
{
    public static class AggregateServiceCollectionExtensions
    {
        /// <summary>
        /// Adds an aggregate repository that uses EventStoreDb to persist events 
        /// </summary>
        public static IServiceCollection AddAggregateRepository(this IServiceCollection services,
                                                                          IConfiguration configuration,
                                                                          Action<AggregateRegistrationOptionsBuilder<ReadOnlyMemory<byte>>> buildAggregateOptions,
                                                                          AggregateRegistry<object, object>
                                                                              aggregateRegistry)
        {
            return AddAggregateRepository<object, object>(services, configuration, buildAggregateOptions, aggregateRegistry);
        }

        /// <summary>
        /// Adds an aggregate repository that uses EventStoreDb to persist events 
        /// </summary>
        public static IServiceCollection AddAggregateRepository<TCommandBase, TEventBase>(
            this IServiceCollection services,
            IConfiguration configuration,
            Action<AggregateRegistrationOptionsBuilder<ReadOnlyMemory<byte>>> buildAggregateOptions,
            AggregateRegistry<TCommandBase, TEventBase> aggregateRegistry)
        {
            var optionsBuilder = new AggregateRegistrationOptionsBuilder<ReadOnlyMemory<byte>>();
            buildAggregateOptions(optionsBuilder);
            var aggregateOptions =
                ((IAggregateRegistrationOptionsBuilderInfrastructure<ReadOnlyMemory<byte>>) optionsBuilder).Build();


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

                return AggregateRepository.Create(aggregateOptions.EventsRepository,
                                                  aggregateOptions.SnapshotRepository,
                                                  aggregateRegistry.EventDispatcher,
                                                  aggregateRegistry.AggregateMetadataMap);
            });

            services.AddSingleton(aggregateRegistry.CommandDispatcher);

            return services;
        }
    }

    public static class AggregateRegistrationOptionsExtensions
    {
        public static IAggregateRegistrationOptionsBuilderInfrastructure<TRawData> UseEventStoreDbForEvents<TRawData>(
            this IAggregateRegistrationOptionsBuilderInfrastructure<TRawData> builder)
        {

            var connection = builder.ServiceProvider.GetRequiredService<EventStoreClient>();

            return builder;
        }

        public static IAggregateRegistrationOptionsBuilderInfrastructure<TRawData> UseEventStoreDbForSnapshots<TRawData>(
            this IAggregateRegistrationOptionsBuilderInfrastructure<TRawData> builder)
        {
            return builder;
        }

    }

    public interface IAggregateRegistrationOptionsBuilderInfrastructure<TRawData>
    {
        IConfiguration Configuration { get; }
        IServiceProvider ServiceProvider { get; }

        AggregateRegistrationOptionsBuilder<TRawData> AddEventsRepository(IEventsRepository<TRawData>
                                                                              eventsRepository);

        AggregateRegistrationOptionsBuilder<TRawData> AddSnapshotRepository(ISnapshotRepository snapshotRepository);

        AggregateRegistrationOptionsBuilder<TRawData> AddEventSerializer(
            IEventSerializer<TRawData> eventSerializer);

        AggregateRegistrationOptions<TRawData> Build();
    }

    public class AggregateRegistrationOptionsBuilder<TRawData> : IAggregateRegistrationOptionsBuilderInfrastructure<TRawData>
    {
        public IConfiguration Configuration { get; }
        public IServiceProvider ServiceProvider { get; }
        private IEventsRepository<TRawData> _eventsRepository;
        private ISnapshotRepository _snapshotRepository;
        private IEventSerializer<TRawData> _eventSerializer;

        public AggregateRegistrationOptionsBuilder(IConfiguration configuration, IServiceProvider serviceProvider)
        {
            Configuration = configuration;
            ServiceProvider = serviceProvider;
        }

        AggregateRegistrationOptionsBuilder<TRawData> IAggregateRegistrationOptionsBuilderInfrastructure<TRawData>.AddEventsRepository(IEventsRepository<TRawData>
                                                                                                                                           eventsRepository)
        {
            _eventsRepository = eventsRepository ?? throw new ArgumentNullException(nameof(eventsRepository));

            return this;
        }

        AggregateRegistrationOptionsBuilder<TRawData> IAggregateRegistrationOptionsBuilderInfrastructure<TRawData>.AddSnapshotRepository(ISnapshotRepository snapshotRepository)
        {
            _snapshotRepository = snapshotRepository ?? throw new ArgumentNullException(nameof(snapshotRepository));
            return this;
        }

        AggregateRegistrationOptionsBuilder<TRawData> IAggregateRegistrationOptionsBuilderInfrastructure<TRawData>.AddEventSerializer(
            IEventSerializer<TRawData> eventSerializer)
        {
            _eventSerializer = eventSerializer ?? throw new ArgumentNullException(nameof(eventSerializer));
            return this;
        }

        AggregateRegistrationOptions<TRawData> IAggregateRegistrationOptionsBuilderInfrastructure<TRawData>.Build()
        {
            return new(_eventsRepository, _snapshotRepository, _eventSerializer);
        }
    }

    public class AggregateRegistrationOptions<TRawData>
    {
        public AggregateRegistrationOptions(IEventsRepository<TRawData> eventsRepository, ISnapshotRepository snapshotRepository, IEventSerializer<TRawData> eventSerializer)
        {
            EventsRepository = eventsRepository ?? throw new ArgumentNullException(nameof(eventsRepository));
            SnapshotRepository = snapshotRepository ?? throw new ArgumentNullException(nameof(snapshotRepository));
            EventSerializer = eventSerializer ?? throw new ArgumentNullException(nameof(eventSerializer));
        }

        public IEventsRepository<TRawData> EventsRepository { get; }
        public ISnapshotRepository SnapshotRepository { get; }
        public IEventSerializer<TRawData> EventSerializer { get; }
    }
}
