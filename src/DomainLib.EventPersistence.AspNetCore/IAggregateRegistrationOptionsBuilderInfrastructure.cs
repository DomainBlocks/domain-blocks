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
        IServiceProvider ServiceProvider { get; }
        IServiceCollection ServiceCollection { get; }

        AggregateRegistrationOptionsBuilder<TRawData> AddEventsRepository(Func<IEventSerializer<TRawData>, IEventsRepository<TRawData>>
                                                                              getEventsRepository);

        AggregateRegistrationOptionsBuilder<TRawData> AddSnapshotRepository(Func<IEventSerializer<TRawData>, ISnapshotRepository>
                                                                                getSnapshotRepository);

        AggregateRegistrationOptionsBuilder<TRawData> AddEventSerializer(
            Func<IEventNameMap, IEventSerializer<TRawData>> getEventSerializer);

        AggregateRegistrationOptions<TRawData> Build(IEventNameMap eventNameMap);
    }
}