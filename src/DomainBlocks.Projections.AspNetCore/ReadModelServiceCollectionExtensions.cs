using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Projections.AspNetCore
{
    public static class ReadModelServiceCollectionExtensions
    {
        public static IServiceCollection AddReadModel(this IServiceCollection services,
                                                      IConfiguration configuration,
                                                      Action<ProjectionRegistrationOptionsBuilder>
                                                          buildProjectionOptions)
        {
            return services.AddReadModel<object>(configuration, buildProjectionOptions);
        }

        public static IServiceCollection AddReadModel<TEventBase>(
            this IServiceCollection services,
            IConfiguration configuration,
            Action<ProjectionRegistrationOptionsBuilder> buildProjectionOptions)
        {
            var optionsBuilder = new ProjectionRegistrationOptionsBuilder(services, configuration);
            buildProjectionOptions(optionsBuilder);

            var rawDataType = optionsBuilder.RawDataType;
            var typedBuildMethod =
                typeof(IProjectionRegistrationOptionsBuilderInfrastructure)
                    .GetMethod(nameof(IProjectionRegistrationOptionsBuilderInfrastructure.Build))
                    ?.MakeGenericMethod(rawDataType);

            services.AddHostedService(provider =>
            {
                dynamic projectionOptions = typedBuildMethod?.Invoke(optionsBuilder, new[] { provider as object });

                if (projectionOptions == null)
                {
                    throw new InvalidOperationException("Unable to build projection registrations");
                }

                return new
                    EventDispatcherHostedService<ReadOnlyMemory<byte>, TEventBase>(new ProjectionRegistryBuilder(),
                        projectionOptions.EventPublisher,
                        projectionOptions.EventSerializer,
                        projectionOptions.OnRegisteringProjections);
            });

            return services;
        }
    }
}
