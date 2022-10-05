using System;

namespace Shopping.ReadModel.Db.Model;

public class ShoppingCartSummaryItem
{
    public Guid Id { get; set; }
    public Guid CartId { get; set; }
    public string ItemDescription { get; set; }
}