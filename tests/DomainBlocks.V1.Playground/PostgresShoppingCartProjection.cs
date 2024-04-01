using DomainBlocks.V1.Abstractions;
using DomainBlocks.V1.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Shopping.Domain.Events;
using Shopping.ReadModel;

namespace DomainBlocks.V1.Playground;

public class PostgresShoppingCartProjection : ReadModelProjectionBase<ShoppingCartDbContext>
{
    private readonly IDbContextFactory<ShoppingCartDbContext> _dbContextFactory;

    public PostgresShoppingCartProjection(IDbContextFactory<ShoppingCartDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;

        When<ItemAddedToShoppingCart>(async (v, e, ct) =>
        {
            var item = await v.ShoppingCartSummaryItems.FindAsync(new object[] { e.SessionId, e.Item }, ct);
            if (item != null)
            {
                return;
            }

            v.ShoppingCartSummaryItems.Add(new ShoppingCartSummaryItem
            {
                SessionId = e.SessionId,
                Item = e.Item
            });
        });

        When<ItemRemovedFromShoppingCart>(async (v, e, ct) =>
        {
            var item = await v.ShoppingCartSummaryItems.FindAsync(new object[] { e.SessionId, e.Item }, ct);
            if (item != null)
            {
                v.ShoppingCartSummaryItems.Remove(item);
            }
        });
    }

    public override async Task OnInitializingAsync(CancellationToken cancellationToken)
    {
        var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
    }

    public override Task<ShoppingCartDbContext> GetViewAsync(CancellationToken cancellationToken)
    {
        return _dbContextFactory.CreateDbContextAsync(cancellationToken);
    }

    public override async Task<GlobalPosition?> OnLoadCheckpointAsync(
        ShoppingCartDbContext view, CancellationToken cancellationToken)
    {
        var bookmark = await view.Bookmarks.FindAsync(new object[] { Bookmark.DefaultId }, cancellationToken);
        return bookmark?.Position == null ? null : GlobalPosition.FromInt64(bookmark.Position);
    }

    public override async Task OnSaveCheckpointAsync(
        ShoppingCartDbContext view, GlobalPosition position, CancellationToken cancellationToken)
    {
        var bookmark = await view.Bookmarks.FindAsync(new object[] { Bookmark.DefaultId }, cancellationToken);
        if (bookmark == null)
        {
            bookmark = new Bookmark();
            view.Bookmarks.Add(bookmark);
        }

        bookmark.Position = position.ToInt64();
        await view.SaveChangesAsync(cancellationToken);
    }
}