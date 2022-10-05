using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DomainBlocks.Serialization;
using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework;

public class EntityFrameworkProjectionBuilder<TEvent, TDbContext> : IProjectionBuilder
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;
    private Func<TDbContext, TEvent, EventMetadata, Task> _executeAction;

    // ReSharper disable once StaticMemberInGenericType
    private static readonly ConcurrentDictionary<DbContext, EntityFrameworkProjectionContext> ProjectionContexts = new();
        
    public EntityFrameworkProjectionBuilder(EventProjectionBuilder<TEvent> builder, TDbContext dbContext)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        _dbContext = dbContext;
        builder.RegisterProjectionBuilder(this);

        var projectionContext = ProjectionContexts.GetOrAdd(dbContext,
            context => new EntityFrameworkProjectionContext(context));

        builder.RegisterContextForEvent(projectionContext);
    }

    public EntityFrameworkProjectionBuilder<TEvent, TDbContext> Executes(
        Action<TDbContext, TEvent, EventMetadata> executeAction)
    {
        _executeAction = (dbContext, @event, metadata) =>
        {
            executeAction(dbContext, @event, metadata);
            return Task.CompletedTask;
        };

        return this;
    }

    public EntityFrameworkProjectionBuilder<TEvent, TDbContext> Executes(
        Action<TDbContext, TEvent> executeAction)
    {
        _executeAction = (dbContext, @event, _) =>
        {
            executeAction(dbContext, @event);
            return Task.CompletedTask;
        };

        return this;
    }

    public EntityFrameworkProjectionBuilder<TEvent, TDbContext> ExecutesAsync(
        Func<TDbContext, TEvent, EventMetadata, Task> executeAction)
    {
        _executeAction = executeAction;
        return this;
    }

    public EntityFrameworkProjectionBuilder<TEvent, TDbContext> ExecutesAsync(
        Func<TDbContext, TEvent, Task> executeAction)
    {
        _executeAction = _executeAction = (dbContext, @event, _) => executeAction(dbContext, @event);
        return this;
    }

    public IEnumerable<(Type eventType, RunProjection func)> BuildProjections()
    {
        Task RunProjection(object evt, EventMetadata metadata) => _executeAction(_dbContext, (TEvent) evt, metadata);
        return EnumerableEx.Return((typeof(TEvent), (RunProjection) RunProjection));
    }
}