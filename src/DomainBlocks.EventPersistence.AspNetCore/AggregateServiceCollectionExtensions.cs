using System;
using System.Reflection;
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
                                                                          Action<AggregateRegistrationOptionsBuilder> buildAggregateOptions,
                                                                          AggregateRegistry<object, object>
                                                                              aggregateRegistry)
        {
            return AddAggregateRepository<object, object>(services, configuration, buildAggregateOptions, aggregateRegistry);
        }

        /// <summary>
        /// Adds an aggregate repository to persist events 
        /// </summary>
        public static IServiceCollection AddAggregateRepository<TCommandBase, TEventBase>(
            this IServiceCollection services,
            IConfiguration configuration,
            Action<AggregateRegistrationOptionsBuilder> buildAggregateOptions,
            AggregateRegistry<TCommandBase, TEventBase> aggregateRegistry)
        {
            var optionsBuilder = new AggregateRegistrationOptionsBuilder(services, configuration);
            
            buildAggregateOptions(optionsBuilder);

            var rawDataType = optionsBuilder.RawDataType;
            var typedBuildMethod =
                typeof(IAggregateRegistrationOptionsBuilderInfrastructure)
                    .GetMethod(nameof(IAggregateRegistrationOptionsBuilderInfrastructure.Build))
                    ?.MakeGenericMethod(rawDataType);
            
            services.AddSingleton<IAggregateRepository<TCommandBase, TEventBase>>(provider =>
            {
                dynamic aggregateOptions = typedBuildMethod?.Invoke(optionsBuilder, new []{ provider, aggregateRegistry.EventNameMap as object});

                if (aggregateOptions == null)
                {
                    throw new InvalidOperationException("Unable to build aggregation registrations");
                }

                return AggregateRepository.Create(aggregateOptions.EventsRepository,
                                                  aggregateOptions.SnapshotRepository,
                                                  aggregateRegistry);
            });

            services.AddSingleton(aggregateRegistry.CommandDispatcher);

            return services;
        }
    }
}
