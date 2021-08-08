using Microsoft.EntityFrameworkCore;

namespace DomainLib.Projections.EntityFramework
{
    public interface IEntityFrameworkProjection<out TDbContext> where TDbContext : DbContext
    {
        TDbContext DbContext { get; }
    }
}