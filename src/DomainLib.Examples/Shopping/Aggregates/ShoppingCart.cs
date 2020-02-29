using System;
using System.Collections.Generic;
using DomainLib.Aggregates;
using DomainLib.Examples.Shopping.Commands;
using DomainLib.Examples.Shopping.Events;

namespace DomainLib.Examples.Shopping.Aggregates
{
    // Demonstrates an immutable aggregate, but it could equally be mutable.
    // Note: the immutable implementation could be better. It's just for demo purposes.
    public class ShoppingCart
    {
        private static readonly ApplyEventRouter<ShoppingCart, IDomainEvent> ApplyEventRouter;

        static ShoppingCart()
        {
            var routes = new ApplyEventRouterBuilder<ShoppingCart, IDomainEvent>();
           
            routes.Add<ShoppingCartCreated>((agg, e) => agg.Apply(e));
            routes.Add<ItemAddedToShoppingCart>((agg, e) => agg.Apply(e));
            
            ApplyEventRouter = routes.Build();
        }
        
        public Guid? Id { get; private set; }
        public IReadOnlyList<string> Items { get; private set; } = new List<string>();

        public static ShoppingCart FromEvents(IEnumerable<IDomainEvent> events) =>
            ApplyEventRouter.Route(new ShoppingCart(), events);

        public ICommandResult<ShoppingCart, IDomainEvent> Execute(AddItemToShoppingCart command)
        {
            var context = CommandExecutionContext.Create(this, ApplyEventRouter);

            var isNew = Id == null;
            if (isNew)
            {
                var shoppingCartCreated = new ShoppingCartCreated(command.Id);
                context = context.ApplyEvent(shoppingCartCreated);
            }

            var itemAddedToShoppingCart = new ItemAddedToShoppingCart(command.Id, command.Item);
            context = context.ApplyEvent(itemAddedToShoppingCart);

            return context.Result;
        }

        private ShoppingCart Apply(ShoppingCartCreated @event)
        {
            return new ShoppingCart {Id = @event.Id};
        }

        private ShoppingCart Apply(ItemAddedToShoppingCart @event)
        {
            if (Id != @event.Id)
            {
                throw new InvalidOperationException("Attempted to add an item for a shopping cart with a different ID");
            }
            
            var newItems = new List<string>(Items) {@event.Item};
            return new ShoppingCart {Id = Id, Items = newItems};
        }
    }
}