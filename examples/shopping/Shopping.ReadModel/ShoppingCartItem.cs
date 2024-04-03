namespace Shopping.ReadModel;

public class ShoppingCartItem
{
    public Guid SessionId { get; set; }
    public string Name { get; set; } = null!;
}