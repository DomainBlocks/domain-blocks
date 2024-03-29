namespace Shopping.ReadModel.Db.Model;

public class ShoppingCartSummaryItem
{
    public Guid SessionId { get; set; }
    public string? Item { get; set; }
}