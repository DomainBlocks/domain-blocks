using System;
using DomainLib.Aggregates;
using DomainLib.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DomainLib.Persistence.AspNetCore
{
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