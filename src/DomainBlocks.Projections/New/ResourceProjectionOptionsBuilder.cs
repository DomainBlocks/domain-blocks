using System;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public class ResourceProjectionOptionsBuilder<TResource> : IProjectionOptionsBuilder where TResource : IDisposable
{
    private ResourceProjectionOptions<TResource> _options;

    public ResourceProjectionOptionsBuilder(Func<TResource> resourceFactory)
    {
        _options = new ResourceProjectionOptions<TResource>().WithResourceFactory(resourceFactory);
    }

    public IProjectionOptions Options => _options;

    public ResourceProjectionOptionsBuilder<TResource> OnInitializing(
        Func<TResource, CancellationToken, Task> onInitializing)
    {
        _options = _options.WithOnInitializing(onInitializing);
        return this;
    }

    public ResourceProjectionOptionsBuilder<TResource> OnSubscribing(
        Func<TResource, CancellationToken, Task<IStreamPosition>> onSubscribing)
    {
        _options = _options.WithOnSubscribing(onSubscribing);
        return this;
    }

    public ResourceProjectionOptionsBuilder<TResource> OnSave(
        Func<TResource, IStreamPosition, CancellationToken, Task> onSave)
    {
        _options = _options.WithOnSave(onSave);
        return this;
    }

    public ResourceProjectionOptionsBuilder<TResource> When<TEvent>(
        Func<TEvent, IEventHandlerContext<TResource>, CancellationToken, Task> projection)
    {
        _options = _options.WithProjection(projection);
        return this;
    }

    public ResourceProjectionOptionsBuilder<TResource> When<TEvent>(
        Action<TEvent, IEventHandlerContext<TResource>> projection)
    {
        _options = _options.WithProjection(projection);
        return this;
    }
}