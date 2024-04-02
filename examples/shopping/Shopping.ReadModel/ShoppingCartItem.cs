namespace Shopping.ReadModel;

public class ShoppingCartItem
{
    public int Id { get; set; }
    public Guid SessionId { get; set; }
    public string Name { get; set; } = null!;
}