using System.Linq;
using System.Threading.Tasks;
using DomainLib.Projections;
using DomainLib.Projections.EntityFramework;
using Shopping.Domain.Events;
using Shopping.ReadModel.Db.Model;

namespace Shopping.ReadModel.Db
{
    public class ShoppingClassSummaryEfProjection : IEntityFrameworkProjection<ShoppingCartDbContext>
    {
        public static void Register(ProjectionRegistryBuilder builder, ShoppingCartDbContext dbContext)
        {
            var shoppingCartSummary = new ShoppingClassSummaryEfProjection(dbContext);

            builder.Event<ItemAddedToShoppingCart>()
                   .FromName(ItemAddedToShoppingCart.EventName)
                   .ToEfProjection<ItemAddedToShoppingCart, ShoppingClassSummaryEfProjection, ShoppingCartDbContext>(shoppingCartSummary)
                   .Executes((context, evt) =>
                   {
                       context.ShoppingCartSummaryItems.Add(new ShoppingCartSummaryItem
                       {
                           CartId = evt.CartId,
                           Id = evt.Id,
                           ItemDescription = evt.Item
                       });

                       return Task.CompletedTask;
                   });

            builder.Event<ItemRemovedFromShoppingCart>()
                   .FromName(ItemRemovedFromShoppingCart.EventName)
                   .ToEfProjection<ItemRemovedFromShoppingCart, ShoppingClassSummaryEfProjection, ShoppingCartDbContext>(shoppingCartSummary)
                   .Executes(async (context, evt) =>
                   {
                       var item = await context.ShoppingCartSummaryItems.FindAsync(evt.Id);

                       if (item != null)
                       {
                           context.ShoppingCartSummaryItems.Remove(item);
                       }
                   });
        }

        private ShoppingClassSummaryEfProjection(ShoppingCartDbContext dbContext)
        {
            DbContext = dbContext;
        }

        public ShoppingCartDbContext DbContext { get; }
    }
}