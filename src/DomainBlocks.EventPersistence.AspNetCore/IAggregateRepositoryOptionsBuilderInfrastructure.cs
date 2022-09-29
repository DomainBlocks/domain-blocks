using System;
using DomainBlocks.Core;
using DomainBlocks.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Persistence.AspNetCore;

public interface IAggregateRepositoryOptionsBuilderInfrastructure
{
    AggregateRepositoryOptionsBuilder<TRawData> RawEventDataType<TRawData>();
    
    AggregateRepositoryOptions<TRawData> Build<TRawData>(IServiceProvider serviceProvider, IEventNameMap eventNameMap);
}

public interface IAggregateRepositoryOptionsBuilderInfrastructure<TRawData>
{
    IConfiguration Configuration { get; }
    IServiceCollection ServiceCollection { get; }

    AggregateRepositoryOptionsBuilder<TRawData> AddEventsRepository(
        Func<IServiceProvider, IEventSerializer<TRawData>, IEventsRepository<TRawData>> getEventsRepository);

    AggregateRepositoryOptionsBuilder<TRawData> AddSnapshotRepository(
        Func<IServiceProvider, IEventSerializer<TRawData>, ISnapshotRepository> getSnapshotRepository);

    AggregateRepositoryOptionsBuilder<TRawData> AddEventSerializer(
        Func<IEventNameMap, IEventSerializer<TRawData>> getEventSerializer);

    AggregateRepositoryOptions<TRawData> Build(IServiceProvider serviceProvider, IEventNameMap eventNameMap);
}