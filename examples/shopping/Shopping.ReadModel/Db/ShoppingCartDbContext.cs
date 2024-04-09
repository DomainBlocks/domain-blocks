using Microsoft.EntityFrameworkCore;
using Shopping.ReadModel.Model;

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

        modelBuilder.Entity<ShoppingCart>(e =>
        {
            e.HasKey(x => x.SessionId);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.SessionId);
        });

        modelBuilder.Entity<ShoppingCartItem>().HasKey(x => new { x.SessionId, x.Name });
        modelBuilder.Entity<ShoppingCartSummary>().HasKey(x => x.SessionId);
        modelBuilder.Entity<Checkpoint>().HasKey(x => x.Name);
    }

    public DbSet<ShoppingCart> ShoppingCarts { get; set; } = null!;
    public DbSet<ShoppingCartItem> ShoppingCartItems { get; set; } = null!;
    public DbSet<ShoppingCartSummary> ShoppingCartSummaries { get; set; } = null!;
    public DbSet<Checkpoint> Checkpoints { get; set; } = null!;
}