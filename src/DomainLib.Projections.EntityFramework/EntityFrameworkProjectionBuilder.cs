using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace DomainLib.Projections.EntityFramework
{
    public class EntityFrameworkProjectionBuilder<TEvent, TProjection, TContext> : IProjectionBuilder where TProjection: IEntityFrameworkProjection<TContext>
        where TContext : DbContext
    {
        private readonly TProjection _projection;
        private Func<TContext, TEvent, Task> _executeAction;
        
        public EntityFrameworkProjectionBuilder(EventProjectionBuilder<TEvent> builder, TProjection projection)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            _projection = projection ?? throw new ArgumentNullException(nameof(projection));
            builder.RegisterProjectionBuilder(this);
            builder.RegisterContextForEvent(new EntityFrameworkContext(_projection.DbContext));
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