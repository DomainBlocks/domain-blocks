using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Projections.New;
using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework.New;

public class DbContextProjectionOptions<TResource, TDbContext> : ResourceProjectionOptions<TResource>
    where TResource : IDisposable where TDbContext : DbContext
{
    private readonly List<(Type, Func<object, TDbContext, Task>)> _eventHandlers = new();

    public Func<TResource, TDbContext> DbContextFactory { get; private set; }
    public Func<TDbContext, CancellationToken, Task> OnInitializing { get; private set; }
    public IEnumerable<(Type, Func<object, TDbContext, Task>)> EventHandlers => _eventHandlers;

    public void WithDbContextFactory(Func<TResource, TDbContext> dbContextFactory)
    {
        DbContextFactory = dbContextFactory;
    }

    public void WithOnInitializing(Func<TDbContext, CancellationToken, Task> onInitializing)
    {
        OnInitializing = onInitializing;
    }

    public void WithEventHandler<TEvent>(Func<TEvent, TDbContext, Task> eventHandler)
    {
        _eventHandlers.Add((typeof(TEvent), (e, dbContext) => eventHandler((TEvent)e, dbContext)));
    }

    public override ProjectionRegistry ToProjectionRegistry()
    {
        var projectionContext = new DbContextProjectionContext<TDbContext>(
            OnInitializing,
            () => ResourceFactory(),
            x => DbContextFactory((TResource)x));

        var eventProjectionMap = new EventProjectionMap();
        var projectionContextMap = new ProjectionContextMap();

        foreach (var (eventType, handler) in EventHandlers)
        {
            var projectionFunc = projectionContext.BindProjectionFunc(handler);
            eventProjectionMap.AddProjectionFunc(eventType, projectionFunc);
            projectionContextMap.RegisterProjectionContext(eventType, projectionContext);
        }

        return new ProjectionRegistry(eventProjectionMap, projectionContextMap, EventNameMap);
    }
}