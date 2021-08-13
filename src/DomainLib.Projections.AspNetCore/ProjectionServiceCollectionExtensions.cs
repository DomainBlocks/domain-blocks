using System;
using DomainLib.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DomainLib.Projections.AspNetCore
{
    public static class ReadModelServiceCollectionExtensions
    {
        public static IServiceCollection AddReadModel<TDbContext>(this IServiceCollection services,
                                                                  IConfiguration configuration,
                                                                  Action<ProjectionRegistrationOptionsBuilder<ReadOnlyMemory<byte>>> buildProjectionOptions,
                                                                  Action<ProjectionRegistryBuilder, TDbContext>
                                                                      onRegisteringProjections)
            where TDbContext : DbContext
        {
            return services.AddReadModel<object, TDbContext>(configuration, buildProjectionOptions, onRegisteringProjections);
        }

        public static IServiceCollection AddReadModel<TEventBase, TDbContext>(
            this IServiceCollection services,
            IConfiguration configuration,
            Action<ProjectionRegistrationOptionsBuilder<ReadOnlyMemory<byte>>> buildProjectionOptions,
            Action<ProjectionRegistryBuilder, TDbContext> onRegisteringProjections) where TDbContext : DbContext
        {
            services.AddHostedService(provider =>
            {
                var optionsBuilder = new ProjectionRegistrationOptionsBuilder<ReadOnlyMemory<byte>>(services, configuration, provider);
                buildProjectionOptions(optionsBuilder);

                var projectionOptions =
                    ((IProjectionRegistrationOptionsBuilderInfrastructure<ReadOnlyMemory<byte>>)optionsBuilder).Build();

                var dispatcherScope = provider.CreateScope();
                var dbContext = dispatcherScope.ServiceProvider.GetRequiredService<TDbContext>();

                return new
                    EventDispatcherHostedService<ReadOnlyMemory<byte>, TEventBase>(new ProjectionRegistryBuilder(),
                        projectionOptions.EventPublisher,
                        projectionOptions.EventSerializer,
                        x => onRegisteringProjections(x, dbContext));
            });

            return services;
        }
    }

    public interface IProjectionRegistrationOptionsBuilderInfrastructure<TRawData>
    {
        IConfiguration Configuration { get; }
        IServiceProvider ServiceProvider { get; }
        IServiceCollection ServiceCollection { get; }

        ProjectionRegistrationOptionsBuilder<TRawData> AddEventPublisher(
            Func<IEventPublisher<TRawData>> getEventPublisher);

        ProjectionRegistrationOptionsBuilder<TRawData> AddEventDeserializer(
            Func<IEventDeserializer<TRawData>> getEventDeserializer);

        ProjectionRegistrationOptions<TRawData> Build();
    }
    
    public class ProjectionRegistrationOptionsBuilder<TRawData> : IProjectionRegistrationOptionsBuilderInfrastructure<TRawData>
    {
        private Func<IEventPublisher<TRawData>> _getEventPublisher;
        private Func<IEventDeserializer<TRawData>> _getEventDeserializer;

        public ProjectionRegistrationOptionsBuilder(IServiceCollection serviceCollection,
                                                    IConfiguration configuration,
                                                    IServiceProvider serviceProvider)
        {
            ServiceCollection = serviceCollection;
            Configuration = configuration;
            ServiceProvider = serviceProvider;
        }

        public IConfiguration Configuration { get; }
        public IServiceProvider ServiceProvider { get; }
        public IServiceCollection ServiceCollection { get; }

        ProjectionRegistrationOptionsBuilder<TRawData> IProjectionRegistrationOptionsBuilderInfrastructure<TRawData>.AddEventPublisher(Func<IEventPublisher<TRawData>> getEventPublisher)
        {
            _getEventPublisher = getEventPublisher;
            return this;
        }

        ProjectionRegistrationOptionsBuilder<TRawData> IProjectionRegistrationOptionsBuilderInfrastructure<TRawData>.AddEventDeserializer(Func<IEventDeserializer<TRawData>> getEventDeserializer)
        {
            _getEventDeserializer = getEventDeserializer;
            return this;
        }

        ProjectionRegistrationOptions<TRawData> IProjectionRegistrationOptionsBuilderInfrastructure<TRawData>.Build()
        {
            var eventPublisher = _getEventPublisher();
            var eventDeserializer = _getEventDeserializer();

            return new ProjectionRegistrationOptions<TRawData>(eventPublisher, eventDeserializer);
        }
    }

    public class ProjectionRegistrationOptions<TRawData>
    {
        public ProjectionRegistrationOptions(IEventPublisher<TRawData> eventPublisher, IEventDeserializer<TRawData> eventSerializer)
        {
            EventPublisher = eventPublisher;
            EventSerializer = eventSerializer;
        }

        public IEventPublisher<TRawData> EventPublisher { get; }
        public IEventDeserializer<TRawData> EventSerializer { get; }
    }
}
