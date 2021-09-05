using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework
{
    public interface IEntityFrameworkProjection<out TDbContext> where TDbContext : DbContext
    {
        TDbContext DbContext { get; }
    }
}