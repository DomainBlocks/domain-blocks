using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace DomainBlocks.Projections.AspNetCore
{
    public interface IEventDispatcherHostedService : IHostedService
    {
    }

    public class EventDispatcherHostedService<TEventBase> : IEventDispatcherHostedService
    {
        private readonly ProjectionRegistryBuilder _registryBuilder;
        private readonly IEventPublisher<TEventBase> _publisher;
        private readonly Action<ProjectionRegistryBuilder> _onRegisteringProjections;

        public EventDispatcherHostedService(ProjectionRegistryBuilder registryBuilder,
                                            IEventPublisher<TEventBase> publisher,
                                            Action<ProjectionRegistryBuilder> onRegisteringProjections)
        {
            _registryBuilder = registryBuilder;
            _publisher = publisher;
            _onRegisteringProjections = onRegisteringProjections;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _onRegisteringProjections(_registryBuilder);
            var projectionRegistry = _registryBuilder.Build();

            var dispatcher = new EventDispatcher<TEventBase>(_publisher,
                                                             projectionRegistry.EventProjectionMap,
                                                             projectionRegistry.ProjectionContextMap,
                                                             EventDispatcherConfiguration.ReadModelDefaults);

            await dispatcher.StartAsync();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}