using DomainBlocks.Projections;
using DomainBlocks.Projections.EntityFramework;
using Shopping.Domain.Events;
using Shopping.ReadModel.Db.Model;

namespace Shopping.ReadModel.Db;

public static class ShoppingClassSummaryEfProjection
{
    public static void Register(ProjectionRegistryBuilder builder, ShoppingCartDbContext dbContext)
    {
        builder.Event<ItemAddedToShoppingCart>()
            .ToEfProjection(dbContext)
            .Executes((context, evt) =>
            {
                context.ShoppingCartSummaryItems.Add(new ShoppingCartSummaryItem
                {
                    CartId = evt.CartId,
                    Id = evt.Id,
                    ItemDescription = evt.Item
                });
            });

        builder.Event<ItemRemovedFromShoppingCart>()
            .ToEfProjection(dbContext)
            .ExecutesAsync(async (context, evt) =>
            {
                var item = await context.ShoppingCartSummaryItems.FindAsync(evt.Id);

                if (item != null)
                {
                    context.ShoppingCartSummaryItems.Remove(item);
                }
            });
    }
}

public static class ShoppingCartHistoryEfProjection
{
    public static void Register(ProjectionRegistryBuilder builder, ShoppingCartDbContext dbContext)
    {
        builder.Event<ShoppingCartCreated>()
            .ToEfProjection(dbContext)
            .Executes((context, evt) =>
            {
                context.ShoppingCartHistory.Add(new ShoppingCartHistory
                {
                    CartId = evt.Id,
                    EventName = "Created"
                });
            });

        builder.Event<ItemAddedToShoppingCart>()
            .ToEfProjection(dbContext)
            .Executes((context, evt) =>
            {
                context.ShoppingCartHistory.Add(new ShoppingCartHistory
                {
                    CartId = evt.CartId,
                    EventName = "Item Added"
                });
            });

        builder.Event<ItemRemovedFromShoppingCart>()
            .ToEfProjection(dbContext)
            .Executes((context, evt) =>
            {
                context.ShoppingCartHistory.Add(new ShoppingCartHistory
                {
                    CartId = evt.CartId,
                    EventName = "Item Removed"
                });
            });
    }
}