using System;
using System.Threading;
using System.Threading.Tasks;
using DomainLib.Projections;
using DomainLib.Serialization;
using Microsoft.Extensions.Hosting;

namespace DomainLib.EventStore.AspNetCore
{
    public class EventDispatcherHostedService<TEventBase> : IHostedService
    {
        private readonly ProjectionRegistryBuilder _registryBuilder;
        private readonly IEventPublisher<ReadOnlyMemory<byte>> _publisher;
        private readonly IEventDeserializer _eventDeserializer;
        private readonly Action<ProjectionRegistryBuilder> _onRegisteringProjections;

        public EventDispatcherHostedService(ProjectionRegistryBuilder registryBuilder,
                                            IEventPublisher<ReadOnlyMemory<byte>> publisher,
                                            IEventDeserializer eventDeserializer,
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

            var dispatcher = new EventDispatcher<TEventBase>(_publisher,
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