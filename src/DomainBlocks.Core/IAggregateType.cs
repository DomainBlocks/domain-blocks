using System;
using System.Collections.Generic;

namespace DomainBlocks.Core;

public interface IAggregateType
{
    public Type ClrType { get; }
    public Type EventBaseType { get; }
    public IEnumerable<IEventType> EventTypes { get; }
}

public interface IAggregateType<TAggregate> : IAggregateType
{
    public Func<TAggregate> Factory { get; }
    public Func<TAggregate, string> IdSelector { get; }
    public Func<string, string> IdToStreamKeySelector { get; }
    public Func<string, string> IdToSnapshotKeySelector { get; }
    public Func<TAggregate, object, TAggregate> EventApplier { get; }

    public string SelectStreamKey(TAggregate aggregate);
    public string SelectSnapshotKey(TAggregate aggregate);

    public ICommandExecutionContext<TAggregate> GetCommandExecutionContext(TAggregate aggregate);
}