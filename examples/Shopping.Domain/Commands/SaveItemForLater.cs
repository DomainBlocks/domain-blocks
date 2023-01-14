using System;

namespace Shopping.Domain.Commands;

public class SaveItemForLater
{
    public SaveItemForLater(Guid id, Guid cartId)
    {
        Id = id;
        CartId = cartId;
    }

    public Guid Id { get; }
    public Guid CartId { get; }
}