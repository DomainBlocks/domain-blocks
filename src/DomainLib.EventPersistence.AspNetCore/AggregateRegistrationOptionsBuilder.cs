using System;
using DomainLib.Aggregates;
using DomainLib.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DomainLib.Persistence.AspNetCore
{
    public class AggregateRegistrationOptionsBuilder<TRawData> : IAggregateRegistrationOptionsBuilderInfrastructure<TRawData>
    {
        public IConfiguration Configuration { get; }
        public IServiceProvider ServiceProvider { get; }
        public IServiceCollection ServiceCollection { get; }
        private Func<IEventSerializer<TRawData>, IEventsRepository<TRawData>> _getEventsRepository;
        private Func<IEventSerializer<TRawData>, ISnapshotRepository>  _getSnapshotRepository;
        private Func<IEventNameMap, IEventSerializer<TRawData>> _getEventSerializer;

        public AggregateRegistrationOptionsBuilder(IServiceCollection serviceCollection, IConfiguration configuration, IServiceProvider serviceProvider)
        {
            ServiceCollection = serviceCollection;
            Configuration = configuration;
            ServiceProvider = serviceProvider;
        }

        AggregateRegistrationOptionsBuilder<TRawData> IAggregateRegistrationOptionsBuilderInfrastructure<TRawData>.
            AddEventsRepository(Func<IEventSerializer<TRawData>, IEventsRepository<TRawData>> getEventsRepository)
        {
            _getEventsRepository = getEventsRepository ?? throw new ArgumentNullException(nameof(getEventsRepository));

            return this;
        }

        AggregateRegistrationOptionsBuilder<TRawData> IAggregateRegistrationOptionsBuilderInfrastructure<TRawData>.
            AddSnapshotRepository(Func<IEventSerializer<TRawData>, ISnapshotRepository> getSnapshotRepository)
        {
            _getSnapshotRepository =
                getSnapshotRepository ?? throw new ArgumentNullException(nameof(getSnapshotRepository));
            return this;
        }

        AggregateRegistrationOptionsBuilder<TRawData> IAggregateRegistrationOptionsBuilderInfrastructure<TRawData>.AddEventSerializer(
            Func<IEventNameMap, IEventSerializer<TRawData>> getEventSerializer)
        {
            _getEventSerializer = getEventSerializer ?? throw new ArgumentNullException(nameof(getEventSerializer));
            return this;
        }

        AggregateRegistrationOptions<TRawData> IAggregateRegistrationOptionsBuilderInfrastructure<TRawData>.Build(IEventNameMap eventNameMap)
        {
            var serializer = _getEventSerializer(eventNameMap);
            var eventsRepository = _getEventsRepository(serializer);
            var snapshotRepository = _getSnapshotRepository(serializer);

            return new AggregateRegistrationOptions<TRawData>(eventsRepository, snapshotRepository, serializer);
        }
    }
}