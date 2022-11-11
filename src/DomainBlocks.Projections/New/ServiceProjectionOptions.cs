using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public class ServiceProjectionOptions<TResource, TService> : IServiceProjectionOptions<TService>
    where TResource : IDisposable
{
    private readonly List<(Type, Func<object, TService, Task>)> _eventHandlers = new();
    private Func<TResource> _resourceFactory;
    private Func<TResource, TService> _serviceFactory;

    public ServiceProjectionOptions()
    {
    }

    private ServiceProjectionOptions(ServiceProjectionOptions<TResource, TService> copyFrom)
    {
        _eventHandlers = new List<(Type, Func<object, TService, Task>)>(copyFrom._eventHandlers);
        _resourceFactory = copyFrom._resourceFactory;
        _serviceFactory = copyFrom._serviceFactory;
        OnInitializing = copyFrom.OnInitializing ?? ((_, _) => Task.CompletedTask);
        OnCatchingUp = copyFrom.OnCatchingUp ?? ((_, _) => Task.CompletedTask);
        OnCaughtUp = copyFrom.OnCaughtUp ?? ((_, _) => Task.CompletedTask);
        OnEventDispatching = copyFrom.OnEventDispatching ?? ((_, _) => Task.CompletedTask);
        OnEventHandled = copyFrom.OnEventHandled ?? ((_, _) => Task.CompletedTask);
    }

    public Func<IDisposable> ResourceFactory => () => _resourceFactory();
    public Func<IDisposable, TService> ServiceFactory => d => _serviceFactory((TResource)d);
    public Func<TService, CancellationToken, Task> OnInitializing { get; private init; }
    public Func<TService, CancellationToken, Task> OnCatchingUp { get; private init; }
    public Func<TService, CancellationToken, Task> OnCaughtUp { get; private init; }
    public Func<TService, CancellationToken, Task> OnEventDispatching { get; private init; }
    public Func<TService, CancellationToken, Task> OnEventHandled { get; private init; }

    public ServiceProjectionOptions<TResource, TService> WithResourceFactory(Func<TResource> resourceFactory)
    {
        return new ServiceProjectionOptions<TResource, TService>(this) { _resourceFactory = resourceFactory };
    }

    public ServiceProjectionOptions<TResource, TService> WithServiceFactory(
        Func<TResource, TService> serviceFactory)
    {
        return new ServiceProjectionOptions<TResource, TService>(this) { _serviceFactory = serviceFactory };
    }

    public ServiceProjectionOptions<TResource, TService> WithOnInitializing(
        Func<TService, CancellationToken, Task> onInitializing)
    {
        return new ServiceProjectionOptions<TResource, TService>(this) { OnInitializing = onInitializing };
    }

    public ServiceProjectionOptions<TResource, TService> WithOnCatchingUp(
        Func<TService, CancellationToken, Task> onCatchingUp)
    {
        return new ServiceProjectionOptions<TResource, TService>(this) { OnCatchingUp = onCatchingUp };
    }

    public ServiceProjectionOptions<TResource, TService> WithOnCaughtUp(
        Func<TService, CancellationToken, Task> onCatchingUp)
    {
        return new ServiceProjectionOptions<TResource, TService>(this) { OnCaughtUp = onCatchingUp };
    }

    public ServiceProjectionOptions<TResource, TService> WithOnEventDispatching(
        Func<TService, CancellationToken, Task> onEventDispatching)
    {
        return new ServiceProjectionOptions<TResource, TService>(this) { OnEventDispatching = onEventDispatching };
    }

    public ServiceProjectionOptions<TResource, TService> WithOnEventHandled(
        Func<TService, CancellationToken, Task> onEventHandled)
    {
        return new ServiceProjectionOptions<TResource, TService>(this) { OnEventHandled = onEventHandled };
    }

    public ServiceProjectionOptions<TResource, TService> WithEventHandler<TEvent>(
        Func<TEvent, TService, Task> eventHandler)
    {
        var copy = new ServiceProjectionOptions<TResource, TService>(this);
        copy._eventHandlers.Add((typeof(TEvent), (e, service) => eventHandler((TEvent)e, service)));
        return copy;
    }

    public ProjectionRegistry Register(ProjectionRegistry registry)
    {
        var projectionContext = new ServiceProjectionContext<TService>(this);

        foreach (var (eventType, handler) in _eventHandlers)
        {
            var projectionFunc = projectionContext.BindProjectionFunc(handler);

            registry = registry
                .RegisterDefaultEventName(eventType)
                .AddProjectionFunc(eventType, projectionFunc)
                .RegisterProjectionContext(eventType, projectionContext);
        }

        return registry;
    }
}