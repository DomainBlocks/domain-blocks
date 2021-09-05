using System;
using DomainBlocks.Aggregates.Registration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Persistence.AspNetCore
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
            var optionsBuilder =
                new AggregateRegistrationOptionsBuilder<ReadOnlyMemory<byte>>(services, configuration);
            buildAggregateOptions(optionsBuilder);

            services.AddSingleton<IAggregateRepository<TEventBase>>(provider =>
            {
                var aggregateOptions =
                    ((IAggregateRegistrationOptionsBuilderInfrastructure<ReadOnlyMemory<byte>>)optionsBuilder)
                    .Build(provider, aggregateRegistry.EventNameMap);

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
