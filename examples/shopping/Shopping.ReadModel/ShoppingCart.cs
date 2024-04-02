namespace Shopping.ReadModel;

public class ShoppingCart
{
    public Guid SessionId { get; set; }
    public ICollection<ShoppingCartItem> Items { get; set; } = new List<ShoppingCartItem>();
}