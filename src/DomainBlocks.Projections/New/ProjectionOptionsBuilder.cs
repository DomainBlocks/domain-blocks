using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public sealed class ProjectionOptionsBuilder<TState> : IProjectionOptionsBuilder
{
    public ProjectionOptions<TState> Options { get; internal set; } = new();
    IProjectionOptions IProjectionOptionsBuilder.Options => Options;

    public ProjectionOptionsBuilder<TResource, TState> Using<TResource>(Func<TResource> resourceFactory)
        where TResource : IDisposable
    {
        return new ProjectionOptionsBuilder<TResource, TState>(resourceFactory, this);
    }

    public void WithStateFactory(Func<CatchUpSubscriptionStatus, TState> stateFactory)
    {
        Options = Options.WithStateFactory(stateFactory);
    }

    public ProjectionOptionsBuilder<TState> OnInitializing(Func<TState, CancellationToken, Task> onInitializing)
    {
        Options = Options.WithOnInitializing(onInitializing);
        return this;
    }

    public ProjectionOptionsBuilder<TState> OnSubscribing(
        Func<TState, CancellationToken, Task<IStreamPosition>> onSubscribing)
    {
        Options = Options.WithOnSubscribing(onSubscribing);
        return this;
    }

    public ProjectionOptionsBuilder<TState> OnSave(Func<TState, IStreamPosition, CancellationToken, Task> onSave)
    {
        Options = Options.WithOnSave(onSave);
        return this;
    }

    public ProjectionOptionsBuilder<TState> When<TEvent>(
        Func<EventRecord<TEvent>, TState, CancellationToken, Task> handler)
    {
        Options = Options.WithHandler(handler);
        return this;
    }

    public ProjectionOptionsBuilder<TState> When<TEvent>(Action<EventRecord<TEvent>, TState> handler)
    {
        Options = Options.WithHandler(handler);
        return this;
    }

    public ProjectionOptionsBuilder<TState> When<TEvent>(Func<TEvent, TState, CancellationToken, Task> handler)
    {
        Options = Options.WithHandler(handler);
        return this;
    }

    public ProjectionOptionsBuilder<TState> When<TEvent>(Action<TEvent, TState> handler)
    {
        Options = Options.WithHandler(handler);
        return this;
    }

    public ProjectionOptionsBuilder<TState> AddInterceptors(IEnumerable<IEventHandlerInterceptor> interceptors)
    {
        Options = Options.WithInterceptors(interceptors);
        return this;
    }

    public ProjectionOptionsBuilder<TState> AddInterceptors(params IEventHandlerInterceptor[] interceptors)
    {
        Options = Options.WithInterceptors(interceptors);
        return this;
    }
}

public sealed class ProjectionOptionsBuilder<TResource, TState> where TResource : IDisposable
{
    private readonly Func<TResource> _resourceFactory;
    private readonly ProjectionOptionsBuilder<TState> _parentBuilder;

    internal ProjectionOptionsBuilder(Func<TResource> resourceFactory, ProjectionOptionsBuilder<TState> parentBuilder)
    {
        _resourceFactory = resourceFactory ?? throw new ArgumentNullException(nameof(resourceFactory));
        _parentBuilder = parentBuilder ?? throw new ArgumentNullException(nameof(parentBuilder));
    }

    public void WithStateFactory(Func<TResource, CatchUpSubscriptionStatus, TState> stateFactory)
    {
        _parentBuilder.Options = _parentBuilder.Options.WithStateFactory(_resourceFactory, stateFactory);
    }
}