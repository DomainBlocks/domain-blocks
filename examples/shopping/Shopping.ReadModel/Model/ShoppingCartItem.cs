namespace Shopping.ReadModel.Model;

public class ShoppingCartItem
{
    public Guid SessionId { get; set; }
    public string Name { get; set; } = null!;
}