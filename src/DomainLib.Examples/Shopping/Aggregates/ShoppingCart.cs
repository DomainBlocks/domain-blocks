using System;
using System.Collections.Generic;
using DomainLib.Aggregates;
using DomainLib.Examples.Shopping.Commands;
using DomainLib.Examples.Shopping.Events;

namespace DomainLib.Examples.Shopping.Aggregates
{
    using ShoppingCartCommandResult = CommandResult<ShoppingCart, IDomainEvent>;
    
    // Demonstrates an immutable aggregate, but it could equally be mutable.
    // Note: the immutable implementation could be better. It's just for demo purposes.
    public class ShoppingCart
    {
        public static readonly EventRouter<ShoppingCart, IDomainEvent> EventRouter;

        static ShoppingCart()
        {
            EventRouter = new EventRouterBuilder<ShoppingCart, IDomainEvent>
            {
                (ShoppingCart x, ShoppingCartCreated y) => x.Apply(y),
                (ShoppingCart x, ItemAddedToShoppingCart y) => x.Apply(y)
            }.Build();
        }
        
        public Guid? Id { get; private set; }
        public IReadOnlyList<string> Items { get; private set; } = new List<string>();

        public ShoppingCartCommandResult Handle(AddItemToShoppingCart command)
        {
            var result = new ShoppingCartCommandResult(this).WithEventRouter(EventRouter);

            var isNew = Id == null;
            if (isNew)
            {
                var shoppingCartCreated = new ShoppingCartCreated(command.Id);
                result = result.ApplyEvent(shoppingCartCreated);
            }

            var itemAddedToShoppingCart = new ItemAddedToShoppingCart(command.Id, command.Item);
            result = result.ApplyEvent(itemAddedToShoppingCart);

            return result;
        }

        private ShoppingCart Apply(ShoppingCartCreated @event)
        {
            return new ShoppingCart {Id = @event.Id};
        }

        private ShoppingCart Apply(ItemAddedToShoppingCart @event)
        {
            var newItems = new List<string>(Items) {@event.Item};
            return new ShoppingCart {Id = Id, Items = newItems};
        }
    }
}
