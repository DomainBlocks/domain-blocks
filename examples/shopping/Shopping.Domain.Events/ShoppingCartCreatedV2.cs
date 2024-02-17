using System;

namespace Shopping.Domain.Events;

public class ShoppingCartCreatedV2 : IDomainEvent
{
    public ShoppingCartCreatedV2(Guid id, string newField)
    {
        Id = id;
        NewField = newField;
    }

    public Guid Id { get; }
    public string NewField { get; }
}