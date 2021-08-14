using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DomainLib.Projections.AspNetCore
{
    public static class ReadModelServiceCollectionExtensions
    {
        public static IServiceCollection AddReadModel(this IServiceCollection services,
                                                      IConfiguration configuration,
                                                      Action<ProjectionRegistrationOptionsBuilder<ReadOnlyMemory<byte>>>
                                                          buildProjectionOptions)
        {
            return services.AddReadModel<object>(configuration, buildProjectionOptions);
        }

        public static IServiceCollection AddReadModel<TEventBase>(
            this IServiceCollection services,
            IConfiguration configuration,
            Action<ProjectionRegistrationOptionsBuilder<ReadOnlyMemory<byte>>> buildProjectionOptions)
        {
            services.AddHostedService(provider =>
            {
                var optionsBuilder =
                    new ProjectionRegistrationOptionsBuilder<ReadOnlyMemory<byte>>(services, configuration, provider);
                buildProjectionOptions(optionsBuilder);

                var projectionOptions =
                    ((IProjectionRegistrationOptionsBuilderInfrastructure<ReadOnlyMemory<byte>>)optionsBuilder).Build();

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
