using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework
{
    public class EntityFrameworkProjectionBuilder<TEvent, TProjection, TContext> : IProjectionBuilder where TProjection: IEntityFrameworkProjection<TContext>
        where TContext : DbContext
    {
        private readonly TProjection _projection;
        private Func<TContext, TEvent, Task> _executeAction;

        // ReSharper disable once StaticMemberInGenericType
        private static readonly ConcurrentDictionary<DbContext, EntityFrameworkProjectionContext> ProjectionContexts = new();
        
        public EntityFrameworkProjectionBuilder(EventProjectionBuilder<TEvent> builder, TProjection projection)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            _projection = projection ?? throw new ArgumentNullException(nameof(projection));
            builder.RegisterProjectionBuilder(this);

            var projectionContext = ProjectionContexts.GetOrAdd(_projection.DbContext,
                                         dbContext => new EntityFrameworkProjectionContext(dbContext));

            builder.RegisterContextForEvent(projectionContext);
        }

        public EntityFrameworkProjectionBuilder<TEvent, TProjection, TContext> Executes(
            Func<TContext, TEvent, Task> executeAction)
        {
            _executeAction = executeAction;
            return this;
        }

        public IEnumerable<(Type eventType, Type projectionType, RunProjection func)> BuildProjections()
        {
            Task RunProjection(object evt) => _executeAction(_projection.DbContext, (TEvent) evt);
            return EnumerableEx.Return((typeof(TEvent), typeof(TProjection), (RunProjection) RunProjection));
        }
    }
}