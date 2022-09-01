using System;
using System.Collections.Generic;

namespace DomainBlocks.Persistence.New;

public interface IAggregateType
{
    public (Type, Type) Key => (ClrType, EventBaseType);
    Type ClrType { get; }
    Type EventBaseType { get; }
    IEnumerable<IEventType> EventTypes { get; }
}