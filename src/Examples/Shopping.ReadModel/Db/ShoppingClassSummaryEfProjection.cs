using DomainBlocks.Projections;
using DomainBlocks.Projections.EntityFramework;
using Shopping.Domain.Events;
using Shopping.ReadModel.Db.Model;

namespace Shopping.ReadModel.Db
{
    public class ShoppingClassSummaryEfProjection
    {
        public static void Register(ProjectionRegistryBuilder builder, ShoppingCartDbContext dbContext)
        {
            var shoppingCartSummary = new ShoppingClassSummaryEfProjection();

            builder.Event<ItemAddedToShoppingCart>()
                   .ToEfProjection(shoppingCartSummary, dbContext)
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
                   .ToEfProjection(shoppingCartSummary, dbContext)
                   .ExecutesAsync(async (context, evt) =>
                   {
                       var item = await context.ShoppingCartSummaryItems.FindAsync(evt.Id);

                       if (item != null)
                       {
                           context.ShoppingCartSummaryItems.Remove(item);
                       }
                   });
        }

        private ShoppingClassSummaryEfProjection()
        {
        }
    }
}