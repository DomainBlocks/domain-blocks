using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework
{
    public static class EventProjectionBuilderExtensions
    {
        public static EntityFrameworkProjectionBuilder<TEvent, TDbContext> ToEfProjection<TEvent, TDbContext>(
            this EventProjectionBuilder<TEvent> builder, TDbContext dbContext)
            where TDbContext : DbContext
        {
            return new (builder, dbContext);
        }
    }
}
