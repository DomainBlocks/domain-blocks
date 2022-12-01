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

        modelBuilder.Entity<ShoppingCartSummaryItem>()
            .HasKey(i => i.Id);

        modelBuilder.Entity<ShoppingCartHistory>()
            .HasKey(i => i.Id);
    }

    public DbSet<Bookmark> Bookmark { get; set; }

    public DbSet<ShoppingCartSummaryItem> ShoppingCartSummaryItems { get; set; }

    public DbSet<ShoppingCartHistory> ShoppingCartHistory { get; set; }
}