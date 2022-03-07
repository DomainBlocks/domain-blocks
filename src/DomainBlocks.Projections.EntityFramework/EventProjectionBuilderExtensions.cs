using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework
{
    public static class EventProjectionBuilderExtensions
    {
        public static EntityFrameworkProjectionBuilder<TEvent, TProjection, TDbContext> ToEfProjection<TEvent,
            TProjection, TDbContext>(
            this EventProjectionBuilder<TEvent> builder,
            TProjection projection, TDbContext dbContext)
            where TDbContext : DbContext
        {
            return new (builder, projection, dbContext);
        }
    }
}
