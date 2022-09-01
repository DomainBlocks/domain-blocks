using System;

namespace DomainBlocks.Persistence.New;

public interface IAggregateType
{
    public Type ClrType { get; }
    public Type EventBaseType { get; }
    public (Type, Type) Key => (ClrType, EventBaseType);
}