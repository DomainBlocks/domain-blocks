using DomainBlocks.V1.Abstractions;
using DomainBlocks.V1.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Shopping.Domain.Events;
using Shopping.ReadModel;

namespace DomainBlocks.V1.Playground;

public class PostgresShoppingCartProjection :
    IEventStreamConsumer,
    IEventHandler<ShoppingSessionStarted>,
    IEventHandler<ItemAddedToShoppingCart>,
    IEventHandler<ItemRemovedFromShoppingCart>
{
    private readonly IDbContextFactory<ShoppingCartDbContext> _dbContextFactory;
    private ShoppingCartDbContext? _currentDbContext;

    public PostgresShoppingCartProjection(IDbContextFactory<ShoppingCartDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    private ShoppingCartDbContext DbContext => _currentDbContext ??= _dbContextFactory.CreateDbContext();

    public async Task OnEventAsync(EventHandlerContext<ShoppingSessionStarted> context)
    {
        var cart = await DbContext.ShoppingCarts.FindAsync(
            new object?[] { context.Event.SessionId }, cancellationToken: context.CancellationToken);

        if (cart == null)
        {
            cart = new ShoppingCart { SessionId = context.Event.SessionId };
            DbContext.ShoppingCarts.Add(cart);
        }
    }

    public async Task OnEventAsync(EventHandlerContext<ItemAddedToShoppingCart> context)
    {
        var item = await DbContext.ShoppingCartItems.FindAsync(
            new object[] { context.Event.SessionId, context.Event.Item }, context.CancellationToken);

        if (item == null)
        {
            DbContext.ShoppingCartItems.Add(new ShoppingCartItem
            {
                SessionId = context.Event.SessionId,
                Name = context.Event.Item
            });
        }
    }

    public async Task OnEventAsync(EventHandlerContext<ItemRemovedFromShoppingCart> context)
    {
        var itemToRemove = await DbContext.ShoppingCartItems.FindAsync(
            new object[] { context.Event.SessionId, context.Event.Item }, context.CancellationToken);

        if (itemToRemove != null)
        {
            DbContext.ShoppingCartItems.Remove(itemToRemove);
        }
    }

    public async Task OnInitializingAsync(CancellationToken cancellationToken)
    {
        var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
    }

    public async Task<SubscriptionPosition?> OnRestoreAsync(CancellationToken cancellationToken)
    {
        var bookmark = await DbContext.Bookmarks.FindAsync(new object[] { Bookmark.DefaultId }, cancellationToken);
        return bookmark == null ? null : new SubscriptionPosition(bookmark.Position);
    }

    public async Task OnCheckpointAsync(SubscriptionPosition position, CancellationToken cancellationToken)
    {
        if (_currentDbContext == null)
        {
            // No activity since last checkpoint. Nothing to do.
            return;
        }

        var bookmark = await DbContext.Bookmarks.FindAsync(new object[] { Bookmark.DefaultId }, cancellationToken);
        if (bookmark == null)
        {
            bookmark = new Bookmark();
            DbContext.Bookmarks.Add(bookmark);
        }

        bookmark.Position = position.Value;

        await DbContext.SaveChangesAsync(cancellationToken);
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