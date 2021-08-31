using System;
using System.Threading;
using System.Threading.Tasks;
using DomainLib.Serialization;
using Microsoft.Extensions.Hosting;

namespace DomainLib.Projections.AspNetCore
{
    public class EventDispatcherHostedService<TRawData, TEventBase> : IHostedService
    {
        private readonly ProjectionRegistryBuilder _registryBuilder;
        private readonly IEventPublisher<TRawData> _publisher;
        private readonly IEventDeserializer<TRawData> _eventDeserializer;
        private readonly Action<ProjectionRegistryBuilder> _onRegisteringProjections;

        public EventDispatcherHostedService(ProjectionRegistryBuilder registryBuilder,
                                            IEventPublisher<TRawData> publisher,
                                            IEventDeserializer<TRawData> eventDeserializer,
                                            Action<ProjectionRegistryBuilder> onRegisteringProjections)
        {
            _registryBuilder = registryBuilder;
            _publisher = publisher;
            _eventDeserializer = eventDeserializer;
            _onRegisteringProjections = onRegisteringProjections;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _onRegisteringProjections(_registryBuilder);
            var projectionRegistry = _registryBuilder.Build();

            var dispatcher = new EventDispatcher<TRawData, TEventBase>(_publisher,
                                                                       projectionRegistry.EventProjectionMap,
                                                                       projectionRegistry.ProjectionContextMap,
                                                                       _eventDeserializer,
                                                                       projectionRegistry.EventNameMap,
                                                                       EventDispatcherConfiguration.ReadModelDefaults);

            await dispatcher.StartAsync();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}