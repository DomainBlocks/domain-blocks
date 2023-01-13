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

        modelBuilder.Entity<ShoppingCartSummaryItem>()
            .HasKey(i => i.Id);

        modelBuilder.Entity<ShoppingCartHistory>()
            .HasKey(i => i.Id);
    }

    public DbSet<Bookmark> Bookmarks { get; set; }

    public DbSet<ShoppingCartSummaryItem> ShoppingCartSummaryItems { get; set; }

    // TODO (DS): Reinstate history projection in future PR.
    public DbSet<ShoppingCartHistory> ShoppingCartHistory { get; set; }
}