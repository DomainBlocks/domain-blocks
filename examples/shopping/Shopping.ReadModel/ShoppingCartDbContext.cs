using Microsoft.EntityFrameworkCore;

namespace Shopping.ReadModel;

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

        modelBuilder.Entity<Bookmark>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
        });
    }

    public DbSet<ShoppingCart> ShoppingCarts { get; set; } = null!;

    public DbSet<Bookmark> Bookmarks { get; set; } = null!;
}