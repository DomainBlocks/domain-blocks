using System;
using System.Collections.Generic;

namespace DomainBlocks.Core;

public interface IAggregateType
{
    public Type ClrType { get; }
    public Type EventBaseType { get; }
    public IEnumerable<IEventType> EventTypes { get; }

    public string SelectStreamKeyFromId(string id);
    public string SelectSnapshotKeyFromId(string id);
}