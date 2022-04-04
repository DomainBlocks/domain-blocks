using System;
using DomainBlocks.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Projections.AspNetCore
{
    public class ProjectionRegistrationOptionsBuilder : IProjectionRegistrationOptionsBuilderInfrastructure
    {
        private object _typedRegistrationOptionsBuilder;
        private Type _eventBaseType;

        public ProjectionRegistrationOptionsBuilder(IServiceCollection serviceCollection, IConfiguration configuration)
        {
            ServiceCollection = serviceCollection;
            Configuration = configuration;
        }

        public Type EventBaseType => _eventBaseType;

        ProjectionRegistrationOptionsBuilder<TEventBase> IProjectionRegistrationOptionsBuilderInfrastructure.TypedAs<TEventBase>()
        {
            _eventBaseType = typeof(TEventBase);
            _typedRegistrationOptionsBuilder = new ProjectionRegistrationOptionsBuilder<TEventBase>(ServiceCollection, Configuration);
            return (ProjectionRegistrationOptionsBuilder<TEventBase>)_typedRegistrationOptionsBuilder;
        }

        public IConfiguration Configuration { get; }
        public IServiceCollection ServiceCollection { get; }

        public ProjectionRegistrationOptions<TEventBase> Build<TEventBase>(IServiceProvider serviceProvider)
        {
            return ((IProjectionRegistrationOptionsBuilderInfrastructure<TEventBase>)
                _typedRegistrationOptionsBuilder).Build(serviceProvider);
        }
    }

    public class ProjectionRegistrationOptionsBuilder<TEventBase> : ProjectionRegistrationOptionsBuilder, 
        IProjectionRegistrationOptionsBuilderInfrastructure<TEventBase>
    {
        private Func<IServiceProvider, IEventPublisher<TEventBase>> _getEventPublisher;
        private Func<IServiceProvider, IEventDeserializer<TEventBase>> _getEventDeserializer;
        private Action<IServiceProvider, ProjectionRegistryBuilder> _onRegisteringProjections;

        public ProjectionRegistrationOptionsBuilder(IServiceCollection serviceCollection,
                                                    IConfiguration configuration) : base(serviceCollection, configuration)
        {
        }

        ProjectionRegistrationOptionsBuilder<TEventBase> IProjectionRegistrationOptionsBuilderInfrastructure<TEventBase>.
            UseEventPublisher(Func<IServiceProvider, IEventPublisher<TEventBase>> getEventPublisher)
        {
            _getEventPublisher = getEventPublisher ?? throw new ArgumentNullException(nameof(getEventPublisher));
            return this;
        }

        ProjectionRegistrationOptionsBuilder<TEventBase> IProjectionRegistrationOptionsBuilderInfrastructure<TEventBase>.
            UseEventDeserializer(Func<IServiceProvider, IEventDeserializer<TEventBase>> getEventDeserializer)
        {
            _getEventDeserializer =
                getEventDeserializer ?? throw new ArgumentNullException(nameof(getEventDeserializer));
            return this;
        }

        ProjectionRegistrationOptionsBuilder<TEventBase> IProjectionRegistrationOptionsBuilderInfrastructure<TEventBase>.UseProjectionRegistrations(
            Action<IServiceProvider, ProjectionRegistryBuilder> onRegisteringProjections)
        {
            _onRegisteringProjections = onRegisteringProjections;
            return this;
        }

        ProjectionRegistrationOptions<TEventBase> IProjectionRegistrationOptionsBuilderInfrastructure<TEventBase>.Build(IServiceProvider serviceProvider)
        {
            var eventPublisher = _getEventPublisher(serviceProvider);
            var eventDeserializer = _getEventDeserializer(serviceProvider);

            return new ProjectionRegistrationOptions<TEventBase>(eventPublisher,
                                                                 eventDeserializer,
                                                                 builder => _onRegisteringProjections(serviceProvider, builder));
        }
    }
}