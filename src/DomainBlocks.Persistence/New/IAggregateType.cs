using System;
using System.Collections.Generic;

namespace DomainBlocks.Persistence.New;

public interface IAggregateType
{
    public Type ClrType { get; }
    public Type EventBaseType { get; }
    public IEnumerable<IEventType> EventTypes { get; }
}