using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework.New;

public class DbContextProjectionOptions<TResource, TDbContext> :
    IDbContextProjectionOptions<TDbContext>
    where TResource : IDisposable where TDbContext : DbContext
{
    private readonly ProjectionEventNameMap _eventNameMap = new();
    private readonly List<(Type, Func<object, TDbContext, Task>)> _eventHandlers = new();
    private Func<TResource> _resourceFactory;
    private Func<TResource, TDbContext> _dbContextFactory;

    public DbContextProjectionOptions()
    {
    }

    private DbContextProjectionOptions(DbContextProjectionOptions<TResource, TDbContext> copyFrom)
    {
        _eventNameMap = new ProjectionEventNameMap(copyFrom._eventNameMap);
        _eventHandlers = new List<(Type, Func<object, TDbContext, Task>)>(copyFrom._eventHandlers);
        _resourceFactory = copyFrom._resourceFactory;
        _dbContextFactory = copyFrom._dbContextFactory;
        OnInitializing = copyFrom.OnInitializing ?? ((_, _) => Task.CompletedTask);
        OnCatchingUp = copyFrom.OnCatchingUp ?? ((_, _) => Task.CompletedTask);
        OnCaughtUp = copyFrom.OnCaughtUp ?? ((_, _) => Task.CompletedTask);
        OnSaved = copyFrom.OnSaved ?? ((_, _) => Task.CompletedTask);
        CatchUpMode = copyFrom.CatchUpMode;
    }

    public Func<IDisposable> ResourceFactory => () => _resourceFactory();
    public Func<IDisposable, TDbContext> DbContextFactory => d => _dbContextFactory((TResource)d);
    public Func<TDbContext, CancellationToken, Task> OnInitializing { get; private init; }
    public Func<TDbContext, CancellationToken, Task> OnCatchingUp { get; private init; }
    public Func<TDbContext, CancellationToken, Task> OnCaughtUp { get; private init; }
    public Func<TDbContext, CancellationToken, Task> OnSaved { get; private init; }
    public DbContextProjectionCatchUpMode CatchUpMode { get; private init; }

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
        return new DbContextProjectionOptions<TResource, TDbContext>(this) { OnInitializing = onInitializing };
    }

    public DbContextProjectionOptions<TResource, TDbContext> WithOnCatchingUp(
        Func<TDbContext, CancellationToken, Task> onCatchingUp)
    {
        return new DbContextProjectionOptions<TResource, TDbContext>(this) { OnCatchingUp = onCatchingUp };
    }
    public DbContextProjectionOptions<TResource, TDbContext> WithOnCaughtUp(
        Func<TDbContext, CancellationToken, Task> onCatchingUp)
    {
        return new DbContextProjectionOptions<TResource, TDbContext>(this) { OnCaughtUp = onCatchingUp };
    }
    
    public DbContextProjectionOptions<TResource, TDbContext> WithOnSaved(
        Func<TDbContext, CancellationToken, Task> onSaved)
    {
        return new DbContextProjectionOptions<TResource, TDbContext>(this) { OnSaved = onSaved };
    }

    public DbContextProjectionOptions<TResource, TDbContext> WithEventHandler<TEvent>(
        Func<TEvent, TDbContext, Task> eventHandler)
    {
        var copy = new DbContextProjectionOptions<TResource, TDbContext>(this);
        copy._eventNameMap.RegisterDefaultEventName<TEvent>();
        copy._eventHandlers.Add((typeof(TEvent), (e, dbContext) => eventHandler((TEvent)e, dbContext)));
        return copy;
    }

    public DbContextProjectionOptions<TResource, TDbContext> WithCatchUpMode(DbContextProjectionCatchUpMode catchUpMode)
    {
        var copy = new DbContextProjectionOptions<TResource, TDbContext>(this) { CatchUpMode = catchUpMode };
        return copy;
    }

    public ProjectionRegistry ToProjectionRegistry()
    {
        var projectionContext = new DbContextProjectionContext<TDbContext>(this);
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