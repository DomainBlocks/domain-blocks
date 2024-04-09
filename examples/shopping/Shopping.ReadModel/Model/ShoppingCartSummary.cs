namespace Shopping.ReadModel.Model;

public class ShoppingCartSummary
{
    public Guid SessionId { get; set; }
    public int ItemCount { get; set; }
}