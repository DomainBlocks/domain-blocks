using System;
using DomainLib.Aggregates.Registration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DomainLib.Persistence.AspNetCore
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
            services.AddSingleton<IAggregateRepository<TEventBase>>(provider =>
            {
                var optionsBuilder =
                    new AggregateRegistrationOptionsBuilder<ReadOnlyMemory<byte>>(services, configuration, provider);
                buildAggregateOptions(optionsBuilder);

                var aggregateOptions =
                    ((IAggregateRegistrationOptionsBuilderInfrastructure<ReadOnlyMemory<byte>>)optionsBuilder)
                    .Build(aggregateRegistry.EventNameMap);

                return AggregateRepository.Create(aggregateOptions.EventsRepository,
                                                  aggregateOptions.SnapshotRepository,
                                                  aggregateRegistry.EventDispatcher,
                                                  aggregateRegistry.AggregateMetadataMap);
            });

            services.AddSingleton(aggregateRegistry.CommandDispatcher);

            return services;
        }
    }
}
