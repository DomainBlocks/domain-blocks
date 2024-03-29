using Microsoft.EntityFrameworkCore;
using Shopping.ReadModel.Db.Model;

namespace Shopping.ReadModel.Db;

public class ShoppingCartDbContext : DbContext
{
    protected ShoppingCartDbContext()
    {
    }

    public ShoppingCartDbContext(DbContextOptions options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Bookmark>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<ShoppingCartSummaryItem>().HasKey(i => new { i.SessionId, i.Item });
    }

    public DbSet<Bookmark> Bookmarks { get; set; } = null!;

    public DbSet<ShoppingCartSummaryItem> ShoppingCartSummaryItems { get; set; } = null!;
}