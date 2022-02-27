using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework
{
    public class EntityFrameworkProjectionBuilder<TEvent, TProjection, TDbContext> : IProjectionBuilder
        where TDbContext : DbContext
    {
        private readonly TProjection _projection;
        private readonly TDbContext _dbContext;
        private Func<TDbContext, TEvent, Task> _executeAction;

        // ReSharper disable once StaticMemberInGenericType
        private static readonly ConcurrentDictionary<DbContext, EntityFrameworkProjectionContext> ProjectionContexts = new();
        
        public EntityFrameworkProjectionBuilder(EventProjectionBuilder<TEvent> builder, TProjection projection, TDbContext dbContext)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            _projection = projection;
            _dbContext = dbContext;
            builder.RegisterProjectionBuilder(this);

            var projectionContext = ProjectionContexts.GetOrAdd(dbContext,
                                                                context => new EntityFrameworkProjectionContext(context));

            builder.RegisterContextForEvent(projectionContext);
        }

        public EntityFrameworkProjectionBuilder<TEvent, TProjection, TDbContext> Executes(
            Action<TDbContext, TEvent> executeAction)
        {
            _executeAction = (dbContext, @event) =>
            {
                executeAction(dbContext, @event);
                return Task.CompletedTask;
            };

            return this;
        }

        public EntityFrameworkProjectionBuilder<TEvent, TProjection, TDbContext> ExecutesAsync(
            Func<TDbContext, TEvent, Task> executeAction)
        {
            _executeAction = executeAction;
            return this;
        }

        public IEnumerable<(Type eventType, Type projectionType, RunProjection func)> BuildProjections()
        {
            Task RunProjection(object evt) => _executeAction(_dbContext, (TEvent) evt);
            return EnumerableEx.Return((typeof(TEvent), typeof(TProjection), (RunProjection) RunProjection));
        }
    }
}