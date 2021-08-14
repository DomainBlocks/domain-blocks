using System;
using DomainLib.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DomainLib.Projections.AspNetCore
{
    public class ProjectionRegistrationOptionsBuilder<TRawData> : IProjectionRegistrationOptionsBuilderInfrastructure<TRawData>
    {
        private Func<IEventPublisher<TRawData>> _getEventPublisher;
        private Func<IEventDeserializer<TRawData>> _getEventDeserializer;
        private Action<ProjectionRegistryBuilder> _onRegisteringProjections;

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
            _getEventPublisher = getEventPublisher ?? throw new ArgumentNullException(nameof(getEventPublisher));
            return this;
        }

        ProjectionRegistrationOptionsBuilder<TRawData> IProjectionRegistrationOptionsBuilderInfrastructure<TRawData>.AddEventDeserializer(Func<IEventDeserializer<TRawData>> getEventDeserializer)
        {
            _getEventDeserializer = getEventDeserializer ?? throw new ArgumentNullException(nameof(getEventDeserializer));
            return this;
        }

        ProjectionRegistrationOptionsBuilder<TRawData> IProjectionRegistrationOptionsBuilderInfrastructure<TRawData>.AddProjectionRegistrations(
            Action<ProjectionRegistryBuilder> onRegisteringProjections)
        {
            _onRegisteringProjections = onRegisteringProjections;
            return this;
        }

        ProjectionRegistrationOptions<TRawData> IProjectionRegistrationOptionsBuilderInfrastructure<TRawData>.Build()
        {
            var eventPublisher = _getEventPublisher();
            var eventDeserializer = _getEventDeserializer();

            return new ProjectionRegistrationOptions<TRawData>(eventPublisher, eventDeserializer, _onRegisteringProjections);
        }
    }
}