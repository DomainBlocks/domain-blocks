namespace Shopping.Events;

public interface IDomainEventVisitor
{
    public void Visit(ItemAddedToShoppingCart @event);
    public void Visit(ItemRemovedFromShoppingCart @event);
    public void Visit(ShoppingCartCreated @event);
}