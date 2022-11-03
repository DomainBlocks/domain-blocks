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

public interface IAggregateType<TAggregate> : IAggregateType
{
    public TAggregate CreateNew();
    public string SelectId(TAggregate aggregate);
    public string SelectStreamKey(TAggregate aggregate);
    public string SelectSnapshotKey(TAggregate aggregate);

    public TAggregate ApplyEvent(TAggregate aggregate, object @event);
    public ICommandExecutionContext<TAggregate> GetCommandExecutionContext(TAggregate aggregate);
}