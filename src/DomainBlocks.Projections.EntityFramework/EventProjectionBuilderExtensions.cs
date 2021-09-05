using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework
{
    public static class EventProjectionBuilderExtensions
    {
        public static EntityFrameworkProjectionBuilder<TEvent, TProjection, TContext> ToEfProjection<TEvent,
            TProjection, TContext>(
            this EventProjectionBuilder<TEvent> builder,
            TProjection projection) where TContext : DbContext
            where TProjection : IEntityFrameworkProjection<TContext>
        {
            return new(builder, projection);
        }
    }
}
