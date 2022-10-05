using System;
using DomainBlocks.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Projections.AspNetCore;

public class ProjectionRegistrationOptionsBuilder : IProjectionRegistrationOptionsBuilderInfrastructure
{
    private object _typedRegistrationOptionsBuilder;
    private Type _rawDataType;

    public ProjectionRegistrationOptionsBuilder(IServiceCollection serviceCollection, IConfiguration configuration)
    {
        ServiceCollection = serviceCollection;
        Configuration = configuration;
    }

    public Type RawDataType => _rawDataType;

    ProjectionRegistrationOptionsBuilder<TRawData> IProjectionRegistrationOptionsBuilderInfrastructure.TypedAs<TRawData>()
    {
        _rawDataType = typeof(TRawData);
        _typedRegistrationOptionsBuilder = new ProjectionRegistrationOptionsBuilder<TRawData>(ServiceCollection, Configuration);
        return (ProjectionRegistrationOptionsBuilder<TRawData>)_typedRegistrationOptionsBuilder;
    }

    public IConfiguration Configuration { get; }
    public IServiceCollection ServiceCollection { get; }

    public ProjectionRegistrationOptions<TRawData> Build<TRawData>(IServiceProvider serviceProvider)
    {
        return ((IProjectionRegistrationOptionsBuilderInfrastructure<TRawData>)
            _typedRegistrationOptionsBuilder).Build(serviceProvider);
    }
}

public class ProjectionRegistrationOptionsBuilder<TRawData> : ProjectionRegistrationOptionsBuilder, 
    IProjectionRegistrationOptionsBuilderInfrastructure<TRawData>
{
    private Func<IServiceProvider, IEventPublisher<TRawData>> _getEventPublisher;
    private Func<IServiceProvider, IEventDeserializer<TRawData>> _getEventDeserializer;
    private Action<IServiceProvider, ProjectionRegistryBuilder> _onRegisteringProjections;

    public ProjectionRegistrationOptionsBuilder(IServiceCollection serviceCollection,
        IConfiguration configuration) : base(serviceCollection, configuration)
    {
    }

    ProjectionRegistrationOptionsBuilder<TRawData> IProjectionRegistrationOptionsBuilderInfrastructure<TRawData>.
        UseEventPublisher(Func<IServiceProvider, IEventPublisher<TRawData>> getEventPublisher)
    {
        _getEventPublisher = getEventPublisher ?? throw new ArgumentNullException(nameof(getEventPublisher));
        return this;
    }

    ProjectionRegistrationOptionsBuilder<TRawData> IProjectionRegistrationOptionsBuilderInfrastructure<TRawData>.
        UseEventDeserializer(Func<IServiceProvider, IEventDeserializer<TRawData>> getEventDeserializer)
    {
        _getEventDeserializer =
            getEventDeserializer ?? throw new ArgumentNullException(nameof(getEventDeserializer));
        return this;
    }

    ProjectionRegistrationOptionsBuilder<TRawData> IProjectionRegistrationOptionsBuilderInfrastructure<TRawData>.UseProjectionRegistrations(
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