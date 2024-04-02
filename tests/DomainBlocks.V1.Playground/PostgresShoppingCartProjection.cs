using DomainBlocks.V1.Abstractions;
using DomainBlocks.V1.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Shopping.Domain.Events;
using Shopping.ReadModel;

namespace DomainBlocks.V1.Playground;

public class PostgresShoppingCartProjection : CatchUpSubscriptionConsumerBase
{
    private readonly IDbContextFactory<ShoppingCartDbContext> _dbContextFactory;
    private ShoppingCartDbContext? _currentDbContext;

    public PostgresShoppingCartProjection(IDbContextFactory<ShoppingCartDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;

        When<ItemAddedToShoppingCart>(async (e, ct) =>
        {
            var cart = await View.ShoppingCarts.FindAsync(new object[] { e.SessionId }, ct);
            if (cart == null)
            {
                cart = new ShoppingCart
                {
                    SessionId = e.SessionId
                };

                View.ShoppingCarts.Add(cart);
            }

            cart.Items.Add(new ShoppingCartItem { SessionId = e.SessionId, Name = e.Item });
        });

        When<ItemRemovedFromShoppingCart>(async (e, ct) =>
        {
            var cart = await View.ShoppingCarts.FindAsync(new object[] { e.SessionId }, ct);
            if (cart != null)
            {
                var itemToRemove = cart.Items.SingleOrDefault(x => x.Name == e.Item);
                if (itemToRemove != null)
                {
                    cart.Items.Remove(itemToRemove);
                }
            }
        });
    }

    private ShoppingCartDbContext View => _currentDbContext ??= _dbContextFactory.CreateDbContext();

    public override async Task OnInitializingAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
    }

    public override async Task<GlobalPosition?> OnLoadCheckpointAsync(CancellationToken cancellationToken)
    {
        var bookmark = await View.Bookmarks.FindAsync(new object[] { Bookmark.DefaultId }, cancellationToken);
        return bookmark?.Position == null ? null : GlobalPosition.FromInt64(bookmark.Position);
    }

    public override async Task OnSaveCheckpointAsync(GlobalPosition position, CancellationToken cancellationToken)
    {
        if (_currentDbContext == null)
        {
            // Nothing to do
            return;
        }

        var bookmark = await View.Bookmarks.FindAsync(new object[] { Bookmark.DefaultId }, cancellationToken);
        if (bookmark == null)
        {
            bookmark = new Bookmark();
            View.Bookmarks.Add(bookmark);
        }

        bookmark.Position = position.ToInt64();

        await View.SaveChangesAsync(cancellationToken);
        await DisposeCurrentDbContextAsync();
    }

    private async ValueTask DisposeCurrentDbContextAsync()
    {
        if (_currentDbContext != null)
        {
            await _currentDbContext.DisposeAsync();
            _currentDbContext = null;
        }
    }
}