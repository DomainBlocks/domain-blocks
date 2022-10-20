using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Projections.New;
using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework.New;

public class DbContextProjectionOptions<TResource, TDbContext> : IProjectionOptions
    where TResource : IDisposable where TDbContext : DbContext
{
    private readonly ProjectionEventNameMap _eventNameMap = new();
    private readonly List<(Type, Func<object, TDbContext, Task>)> _eventHandlers = new();
    private Func<TResource> _resourceFactory;
    private Func<TResource, TDbContext> _dbContextFactory;
    private Func<TDbContext, CancellationToken, Task> _onInitializing;

    public DbContextProjectionOptions()
    {
    }

    private DbContextProjectionOptions(DbContextProjectionOptions<TResource, TDbContext> copyFrom)
    {
        _eventNameMap = new ProjectionEventNameMap(copyFrom._eventNameMap);
        _eventHandlers = new List<(Type, Func<object, TDbContext, Task>)>(copyFrom._eventHandlers);
        _resourceFactory = copyFrom._resourceFactory;
        _dbContextFactory = copyFrom._dbContextFactory;
        _onInitializing = copyFrom._onInitializing;
    }

    public DbContextProjectionOptions<TResource, TDbContext> WithResourceFactory(Func<TResource> resourceFactory)
    {
        return new DbContextProjectionOptions<TResource, TDbContext>(this) { _resourceFactory = resourceFactory };
    }

    public DbContextProjectionOptions<TResource, TDbContext> WithDbContextFactory(
        Func<TResource, TDbContext> dbContextFactory)
    {
        return new DbContextProjectionOptions<TResource, TDbContext>(this) { _dbContextFactory = dbContextFactory };
    }

    public DbContextProjectionOptions<TResource, TDbContext> WithOnInitializing(
        Func<TDbContext, CancellationToken, Task> onInitializing)
    {
        return new DbContextProjectionOptions<TResource, TDbContext>(this) { _onInitializing = onInitializing };
    }

    public DbContextProjectionOptions<TResource, TDbContext> WithEventHandler<TEvent>(
        Func<TEvent, TDbContext, Task> eventHandler)
    {
        var copy = new DbContextProjectionOptions<TResource, TDbContext>(this);
        copy._eventNameMap.RegisterDefaultEventName<TEvent>();
        copy._eventHandlers.Add((typeof(TEvent), (e, dbContext) => eventHandler((TEvent)e, dbContext)));
        return copy;
    }

    public ProjectionRegistry ToProjectionRegistry()
    {
        var projectionContext = new DbContextProjectionContext<TDbContext>(
            _onInitializing,
            () => _resourceFactory(),
            x => _dbContextFactory((TResource)x));

        var eventProjectionMap = new EventProjectionMap();
        var projectionContextMap = new ProjectionContextMap();

        foreach (var (eventType, handler) in _eventHandlers)
        {
            var projectionFunc = projectionContext.BindProjectionFunc(handler);
            eventProjectionMap.AddProjectionFunc(eventType, projectionFunc);
            projectionContextMap.RegisterProjectionContext(eventType, projectionContext);
        }

        return new ProjectionRegistry(eventProjectionMap, projectionContextMap, _eventNameMap);
    }
}