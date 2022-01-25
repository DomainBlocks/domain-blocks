using System;
using DomainBlocks.Aggregates;
using DomainBlocks.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Persistence.AspNetCore
{

    public interface IAggregateRegistrationOptionsBuilderInfrastructure
    {
        AggregateRegistrationOptionsBuilder<TRawData> RawEventDataType<TRawData>();

        AggregateRegistrationOptions<TRawData> Build<TRawData>(IServiceProvider serviceProvider,
                                                               IEventNameMap eventNameMap);
    }

    public interface IAggregateRegistrationOptionsBuilderInfrastructure<TRawData>
    {
        IConfiguration Configuration { get; }
        IServiceCollection ServiceCollection { get; }

        AggregateRegistrationOptionsBuilder<TRawData> AddEventsRepository(Func<IServiceProvider, IEventSerializer<TRawData>, IEventsRepository<TRawData>>
                                                                              getEventsRepository);

        AggregateRegistrationOptionsBuilder<TRawData> AddSnapshotRepository(Func<IServiceProvider, IEventSerializer<TRawData>, ISnapshotRepository>
                                                                                getSnapshotRepository);

        AggregateRegistrationOptionsBuilder<TRawData> AddEventSerializer(
            Func<IEventNameMap, IEventSerializer<TRawData>> getEventSerializer);

        AggregateRegistrationOptions<TRawData> Build(IServiceProvider serviceProvider, IEventNameMap eventNameMap);
    }
}