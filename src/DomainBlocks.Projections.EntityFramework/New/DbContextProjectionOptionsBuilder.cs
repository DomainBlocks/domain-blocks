using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework.New;

public class DbContextProjectionOptionsBuilder<TDbContext> where TDbContext : DbContext
{
    private readonly ProjectionEventNameMap _eventNameMap = new();
    private readonly List<(Type, Func<object, TDbContext, Task>)> _eventHandlers = new();
    private Func<TDbContext, CancellationToken, Task> _onInitializing;
    private Func<IDisposable> _resourceFactory;
    private Func<IDisposable, TDbContext> _dbContextFactory;

    public UsingBuilder<TResource> Using<TResource>(Func<TResource> resourceFactory) where TResource : IDisposable
    {
        _resourceFactory = () => resourceFactory();
        return new UsingBuilder<TResource>(this);
    }

    public void WithDbContext(Func<TDbContext> dbContextFactory)
    {
        _dbContextFactory = _ => dbContextFactory();
    }

    public void OnInitializing(Func<TDbContext, CancellationToken, Task> onInitializing)
    {
        _onInitializing = onInitializing;
    }

    public void When<TEvent>(Func<TEvent, TDbContext, Task> eventHandler)
    {
        _eventNameMap.RegisterDefaultEventName<TEvent>();
        _eventHandlers.Add((typeof(TEvent), (e, dbContext) => eventHandler((TEvent)e, dbContext)));
    }

    public ProjectionRegistry Build()
    {
        var eventProjectionMap = new EventProjectionMap();
        var projectionContextMap = new ProjectionContextMap();

        var projectionContext = new DbContextProjectionContext<TDbContext>(
            _onInitializing, _resourceFactory, _dbContextFactory);

        foreach (var (eventType, handler) in _eventHandlers)
        {
            var projectionFunc = projectionContext.CreateProjectionFunc(handler);
            eventProjectionMap.AddProjectionFunc(eventType, projectionFunc);
            projectionContextMap.RegisterProjectionContext(eventType, projectionContext);
        }

        return new ProjectionRegistry(eventProjectionMap, projectionContextMap, _eventNameMap);
    }

    public class UsingBuilder<TResource> where TResource : IDisposable
    {
        private readonly DbContextProjectionOptionsBuilder<TDbContext> _parent;

        public UsingBuilder(DbContextProjectionOptionsBuilder<TDbContext> parent)
        {
            _parent = parent;
        }

        public void WithDbContext(Func<TResource, TDbContext> dbContextFactory)
        {
            _parent._dbContextFactory = resource => dbContextFactory((TResource)resource);
        }
    }
}