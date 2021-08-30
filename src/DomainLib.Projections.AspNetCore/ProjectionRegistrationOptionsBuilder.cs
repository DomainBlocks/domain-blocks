using System;
using DomainLib.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DomainLib.Projections.AspNetCore
{
    public class ProjectionRegistrationOptionsBuilder<TRawData> : IProjectionRegistrationOptionsBuilderInfrastructure<TRawData>
    {
        private Func<IServiceProvider, IEventPublisher<TRawData>> _getEventPublisher;
        private Func<IServiceProvider, IEventDeserializer<TRawData>> _getEventDeserializer;
        private Action<IServiceProvider, ProjectionRegistryBuilder> _onRegisteringProjections;

        public ProjectionRegistrationOptionsBuilder(IServiceCollection serviceCollection,
                                                    IConfiguration configuration)
        {
            ServiceCollection = serviceCollection;
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }
        public IServiceCollection ServiceCollection { get; }

        ProjectionRegistrationOptionsBuilder<TRawData> IProjectionRegistrationOptionsBuilderInfrastructure<TRawData>.
            AddEventPublisher(Func<IServiceProvider, IEventPublisher<TRawData>> getEventPublisher)
        {
            _getEventPublisher = getEventPublisher ?? throw new ArgumentNullException(nameof(getEventPublisher));
            return this;
        }

        ProjectionRegistrationOptionsBuilder<TRawData> IProjectionRegistrationOptionsBuilderInfrastructure<TRawData>.
            AddEventDeserializer(Func<IServiceProvider, IEventDeserializer<TRawData>> getEventDeserializer)
        {
            _getEventDeserializer =
                getEventDeserializer ?? throw new ArgumentNullException(nameof(getEventDeserializer));
            return this;
        }

        ProjectionRegistrationOptionsBuilder<TRawData> IProjectionRegistrationOptionsBuilderInfrastructure<TRawData>.AddProjectionRegistrations(
            Action<IServiceProvider, ProjectionRegistryBuilder> onRegisteringProjections)
        {
            _onRegisteringProjections = onRegisteringProjections;
            return this;
        }

        ProjectionRegistrationOptions<TRawData> IProjectionRegistrationOptionsBuilderInfrastructure<TRawData>.Build(IServiceProvider serviceProvider)
        {
            var eventPublisher = _getEventPublisher(serviceProvider);
            var eventDeserializer = _getEventDeserializer(serviceProvider);

            return new ProjectionRegistrationOptions<TRawData>(eventPublisher,
                                                               eventDeserializer,
                                                               builder => _onRegisteringProjections(serviceProvider, builder));
        }
    }
}