using System;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Serialization;
using Microsoft.Extensions.Hosting;

namespace DomainBlocks.Projections.AspNetCore;

public interface IEventDispatcherHostedService : IHostedService
{
}

public class EventDispatcherHostedService<TRawData, TEventBase> : IEventDispatcherHostedService
{
    private readonly ProjectionRegistryBuilder _registryBuilder;
    private readonly IEventPublisher<TRawData> _publisher;
    private readonly IEventDeserializer<TRawData> _eventDeserializer;
    private readonly Action<ProjectionRegistryBuilder> _onRegisteringProjections;

    public EventDispatcherHostedService(
        ProjectionRegistryBuilder registryBuilder,
        IEventPublisher<TRawData> publisher,
        IEventDeserializer<TRawData> eventDeserializer,
        Action<ProjectionRegistryBuilder> onRegisteringProjections)
    {
        _registryBuilder = registryBuilder;
        _publisher = publisher;
        _eventDeserializer = eventDeserializer;
        _onRegisteringProjections = onRegisteringProjections;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _onRegisteringProjections(_registryBuilder);
        var projectionRegistry = _registryBuilder.Build();

        var dispatcher = new EventDispatcher<TRawData, TEventBase>(
            _publisher,
            projectionRegistry.EventProjectionMap,
            projectionRegistry.ProjectionContextMap,
            _eventDeserializer,
            projectionRegistry.EventNameMap,
            EventDispatcherConfiguration.ReadModelDefaults);

        await dispatcher.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

// Simplified event dispatcher which we'll use while we refactor the builder logic.
public class EventDispatcherHostedServiceNew<TRawData, TEventBase> : IEventDispatcherHostedService
{
    private readonly IEventPublisher<TRawData> _publisher;
    private readonly IEventDeserializer<TRawData> _eventDeserializer;
    private readonly ProjectionRegistry _projectionRegistry;

    public EventDispatcherHostedServiceNew(
        IEventPublisher<TRawData> publisher,
        IEventDeserializer<TRawData> eventDeserializer,
        ProjectionRegistry projectionRegistry)
    {
        _publisher = publisher;
        _eventDeserializer = eventDeserializer;
        _projectionRegistry = projectionRegistry;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var dispatcher = new EventDispatcher<TRawData, TEventBase>(
            _publisher,
            _projectionRegistry.EventProjectionMap,
            _projectionRegistry.ProjectionContextMap,
            _eventDeserializer,
            _projectionRegistry.EventNameMap,
            EventDispatcherConfiguration.ReadModelDefaults);

        await dispatcher.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}