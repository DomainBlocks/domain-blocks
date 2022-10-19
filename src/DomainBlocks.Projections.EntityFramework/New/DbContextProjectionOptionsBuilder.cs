using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Projections.New;
using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework.New;

public class DbContextProjectionOptionsBuilder<TResource, TDbContext>
    where TDbContext : DbContext where TResource : IDisposable
{
    private readonly ProjectionOptions<TResource> _options;
    private readonly List<(Type, Func<object, TDbContext, Task>)> _eventHandlers = new();
    private Func<TDbContext, CancellationToken, Task> _onInitializing;

    public DbContextProjectionOptionsBuilder(
        ProjectionOptions<TResource> options, Func<TResource, TDbContext> dbContextFactory)
    {
        _options = options;

        // This feels like we're missing some decent abstractions here.
        options.WithProjectionRegistryFactory(() =>
        {
            var projectionContext = new DbContextProjectionContext<TDbContext>(
                _onInitializing,
                () => options.ResourceFactory(),
                x => dbContextFactory((TResource)x));

            var eventProjectionMap = new EventProjectionMap();
            var projectionContextMap = new ProjectionContextMap();

            foreach (var (eventType, handler) in _eventHandlers)
            {
                var projectionFunc = projectionContext.BindProjectionFunc(handler);
                eventProjectionMap.AddProjectionFunc(eventType, projectionFunc);
                projectionContextMap.RegisterProjectionContext(eventType, projectionContext);
            }

            return new ProjectionRegistry(eventProjectionMap, projectionContextMap, options.EventNameMap);
        });
    }

    public void AddProjection(Action<DbContextProjectionOptionsBuilder<TResource, TDbContext>> optionBuilder)
    {
        optionBuilder(this);
    }

    public void OnInitializing(Func<TDbContext, CancellationToken, Task> onInitializing)
    {
        _onInitializing = onInitializing;
    }

    public void When<TEvent>(Func<TEvent, TDbContext, Task> eventHandler)
    {
        _options.WithDefaultEventName<TEvent>();
        _eventHandlers.Add((typeof(TEvent), (e, dbContext) => eventHandler((TEvent)e, dbContext)));
    }
}