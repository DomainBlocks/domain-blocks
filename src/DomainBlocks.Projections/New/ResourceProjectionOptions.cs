using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public class ResourceProjectionOptions<TResource> : IProjectionOptions where TResource : IDisposable
{
    private readonly List<(Type, Func<object, EventHandlerContext<TResource>, CancellationToken, Task>)> _projections =
        new();

    private Func<TResource> _resourceFactory;
    private Func<TResource, CancellationToken, Task> _onInitializing;
    private Func<TResource, CancellationToken, Task<IStreamPosition>> _onSubscribing;
    private Func<TResource, IStreamPosition, CancellationToken, Task> _onSave;

    public ResourceProjectionOptions()
    {
        _onInitializing = (_, _) => Task.CompletedTask;
        _onSubscribing = (_, _) => Task.FromResult(StreamPosition.Empty);
    }

    private ResourceProjectionOptions(ResourceProjectionOptions<TResource> copyFrom)
    {
        _projections = copyFrom._projections.ToList();
        _resourceFactory = copyFrom._resourceFactory;
        _onInitializing = copyFrom._onInitializing;
        _onSubscribing = copyFrom._onSubscribing;
        _onSave = copyFrom._onSave;
        _resourceFactory = copyFrom._resourceFactory;
    }

    public ResourceProjectionOptions<TResource> WithResourceFactory(Func<TResource> resourceFactory)
    {
        if (resourceFactory == null) throw new ArgumentNullException(nameof(resourceFactory));
        return new ResourceProjectionOptions<TResource>(this) { _resourceFactory = resourceFactory };
    }

    public ResourceProjectionOptions<TResource> WithOnInitializing(
        Func<TResource, CancellationToken, Task> onInitializing)
    {
        if (onInitializing == null) throw new ArgumentNullException(nameof(onInitializing));
        return new ResourceProjectionOptions<TResource>(this) { _onInitializing = onInitializing };
    }

    public ResourceProjectionOptions<TResource> WithOnSubscribing(
        Func<TResource, CancellationToken, Task<IStreamPosition>> onSubscribing)
    {
        if (onSubscribing == null) throw new ArgumentNullException(nameof(onSubscribing));
        return new ResourceProjectionOptions<TResource>(this) { _onSubscribing = onSubscribing };
    }

    public ResourceProjectionOptions<TResource> WithOnSave(
        Func<TResource, IStreamPosition, CancellationToken, Task> onSave)
    {
        if (onSave == null) throw new ArgumentNullException(nameof(onSave));
        return new ResourceProjectionOptions<TResource>(this) { _onSave = onSave };
    }

    public ResourceProjectionOptions<TResource> WithProjection<TEvent>(
        Func<TEvent, EventHandlerContext<TResource>, CancellationToken, Task> projection)
    {
        var copy = new ResourceProjectionOptions<TResource>(this);
        copy._projections.Add((typeof(TEvent), (e, context, ct) => projection((TEvent)e, context, ct)));
        return copy;
    }

    public ResourceProjectionOptions<TResource> WithProjection<TEvent>(
        Action<TEvent, EventHandlerContext<TResource>> projection)
    {
        return WithProjection<TEvent>((e, context, _) =>
        {
            projection(e, context);
            return Task.CompletedTask;
        });
    }

    public ProjectionRegistry Register(ProjectionRegistry registry)
    {
        var projectionContext = new ResourceProjectionContext<TResource>(
            _resourceFactory, _onInitializing, _onSubscribing, _onSave);

        foreach (var (eventType, projection) in _projections)
        {
            var projectionFunc = projectionContext.BindProjection(projection);

            registry = registry
                .RegisterDefaultEventName(eventType)
                .AddProjectionFunc(eventType, projectionFunc)
                .RegisterProjectionContext(eventType, projectionContext);
        }

        return registry;
    }
}