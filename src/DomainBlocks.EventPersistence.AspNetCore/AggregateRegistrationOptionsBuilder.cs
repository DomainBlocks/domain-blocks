using System;
using DomainBlocks.Aggregates;
using DomainBlocks.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Persistence.AspNetCore
{
    public class AggregateRegistrationOptionsBuilder : IAggregateRegistrationOptionsBuilderInfrastructure
    {
        private readonly IServiceCollection _serviceCollection;
        private readonly IConfiguration _configuration;
        private object _typedRegistrationOptionsBuilder;
        private Type _rawDataType;

        public AggregateRegistrationOptionsBuilder(IServiceCollection serviceCollection, IConfiguration configuration)
        {
            _serviceCollection = serviceCollection;
            _configuration = configuration;
        }

        public AggregateRegistrationOptionsBuilder<TRawData> RawEventDataType<TRawData>()
        {
            _rawDataType = typeof(TRawData);
            _typedRegistrationOptionsBuilder = new AggregateRegistrationOptionsBuilder<TRawData>(_serviceCollection, _configuration);
            return (AggregateRegistrationOptionsBuilder<TRawData>)_typedRegistrationOptionsBuilder;
        }

        public Type RawDataType => _rawDataType;
        
        public AggregateRegistrationOptions<TRawData> Build<TRawData>(IServiceProvider serviceProvider,
                                                                      IEventNameMap eventNameMap)
        {
            return ((IAggregateRegistrationOptionsBuilderInfrastructure<TRawData>)
                       _typedRegistrationOptionsBuilder).Build(serviceProvider, eventNameMap);
        }
    }

    public class AggregateRegistrationOptionsBuilder<TRawData> : IAggregateRegistrationOptionsBuilderInfrastructure<TRawData>
    {
        public IConfiguration Configuration { get; }
        public IServiceCollection ServiceCollection { get; }
        private Func<IServiceProvider, IEventSerializer<TRawData>, IEventsRepository<TRawData>> _getEventsRepository;
        private Func<IServiceProvider, IEventSerializer<TRawData>, ISnapshotRepository>  _getSnapshotRepository;
        private Func<IEventNameMap, IEventSerializer<TRawData>> _getEventSerializer;

        public AggregateRegistrationOptionsBuilder(IServiceCollection serviceCollection, IConfiguration configuration)
        {
            ServiceCollection = serviceCollection;
            Configuration = configuration;
        }

        AggregateRegistrationOptionsBuilder<TRawData> IAggregateRegistrationOptionsBuilderInfrastructure<TRawData>.
            AddEventsRepository(Func<IServiceProvider, IEventSerializer<TRawData>, IEventsRepository<TRawData>> getEventsRepository)
        {
            _getEventsRepository = getEventsRepository ?? throw new ArgumentNullException(nameof(getEventsRepository));

            return this;
        }

        AggregateRegistrationOptionsBuilder<TRawData> IAggregateRegistrationOptionsBuilderInfrastructure<TRawData>.
            AddSnapshotRepository(Func<IServiceProvider, IEventSerializer<TRawData>, ISnapshotRepository> getSnapshotRepository)
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

        AggregateRegistrationOptions<TRawData> IAggregateRegistrationOptionsBuilderInfrastructure<TRawData>.Build(
            IServiceProvider serviceProvider,
            IEventNameMap eventNameMap)
        {
            var serializer = _getEventSerializer(eventNameMap);
            var eventsRepository = _getEventsRepository(serviceProvider, serializer);
            var snapshotRepository = _getSnapshotRepository(serviceProvider, serializer);

            return new AggregateRegistrationOptions<TRawData>(eventsRepository, snapshotRepository, serializer);
        }
    }
}