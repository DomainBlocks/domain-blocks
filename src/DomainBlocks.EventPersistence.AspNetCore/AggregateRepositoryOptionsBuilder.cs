using System;
using DomainBlocks.Core;
using DomainBlocks.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Persistence.AspNetCore;

public class AggregateRepositoryOptionsBuilder : IAggregateRepositoryOptionsBuilderInfrastructure
{
    private readonly IServiceCollection _serviceCollection;
    private readonly IConfiguration _configuration;
    private object _typedRegistrationOptionsBuilder;
    private Type _rawDataType;

    public AggregateRepositoryOptionsBuilder(IServiceCollection serviceCollection, IConfiguration configuration)
    {
        _serviceCollection = serviceCollection;
        _configuration = configuration;
    }

    public AggregateRepositoryOptionsBuilder<TRawData> RawEventDataType<TRawData>()
    {
        _rawDataType = typeof(TRawData);
        _typedRegistrationOptionsBuilder =
            new AggregateRepositoryOptionsBuilder<TRawData>(_serviceCollection, _configuration);
        return (AggregateRepositoryOptionsBuilder<TRawData>)_typedRegistrationOptionsBuilder;
    }

    public Type RawDataType => _rawDataType;
        
    public AggregateRepositoryOptions<TRawData> Build<TRawData>(IServiceProvider serviceProvider,
        IEventNameMap eventNameMap)
    {
        return ((IAggregateRepositoryOptionsBuilderInfrastructure<TRawData>)
            _typedRegistrationOptionsBuilder).Build(serviceProvider, eventNameMap);
    }
}

public class AggregateRepositoryOptionsBuilder<TRawData> : IAggregateRepositoryOptionsBuilderInfrastructure<TRawData>
{
    private Func<IServiceProvider, IEventSerializer<TRawData>, IEventsRepository<TRawData>> _getEventsRepository;
    private Func<IServiceProvider, IEventSerializer<TRawData>, ISnapshotRepository>  _getSnapshotRepository;
    private Func<IEventNameMap, IEventSerializer<TRawData>> _getEventSerializer;

    public AggregateRepositoryOptionsBuilder(IServiceCollection serviceCollection, IConfiguration configuration)
    {
        ServiceCollection = serviceCollection;
        Configuration = configuration;
    }
    
    public IConfiguration Configuration { get; }
    public IServiceCollection ServiceCollection { get; }

    AggregateRepositoryOptionsBuilder<TRawData> IAggregateRepositoryOptionsBuilderInfrastructure<TRawData>.
        AddEventsRepository(
            Func<IServiceProvider, IEventSerializer<TRawData>, IEventsRepository<TRawData>> getEventsRepository)
    {
        _getEventsRepository = getEventsRepository ?? throw new ArgumentNullException(nameof(getEventsRepository));

        return this;
    }

    AggregateRepositoryOptionsBuilder<TRawData> IAggregateRepositoryOptionsBuilderInfrastructure<TRawData>.
        AddSnapshotRepository(
            Func<IServiceProvider, IEventSerializer<TRawData>, ISnapshotRepository> getSnapshotRepository)
    {
        _getSnapshotRepository =
            getSnapshotRepository ?? throw new ArgumentNullException(nameof(getSnapshotRepository));
        return this;
    }

    AggregateRepositoryOptionsBuilder<TRawData> IAggregateRepositoryOptionsBuilderInfrastructure<TRawData>.
        AddEventSerializer(Func<IEventNameMap, IEventSerializer<TRawData>> getEventSerializer)
    {
        _getEventSerializer = getEventSerializer ?? throw new ArgumentNullException(nameof(getEventSerializer));
        return this;
    }

    AggregateRepositoryOptions<TRawData> IAggregateRepositoryOptionsBuilderInfrastructure<TRawData>.Build(
        IServiceProvider serviceProvider, IEventNameMap eventNameMap)
    {
        var serializer = _getEventSerializer(eventNameMap);
        var eventsRepository = _getEventsRepository(serviceProvider, serializer);
        var snapshotRepository = _getSnapshotRepository(serviceProvider, serializer);

        return new AggregateRepositoryOptions<TRawData>(eventsRepository, snapshotRepository, serializer);
    }
}