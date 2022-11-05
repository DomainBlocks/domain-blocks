using System;
using System.Collections.Generic;

namespace DomainBlocks.Core;

public interface IAggregateOptions
{
    Type ClrType { get; }
    IEnumerable<IEventOptions> EventsOptions { get; }
}

public interface IAggregateOptions<TAggregate> : IAggregateOptions
{
    TAggregate CreateNew();
    string MakeStreamKey(string id);
    string MakeSnapshotKey(string id);
    string MakeSnapshotKey(TAggregate aggregate);
    ICommandExecutionContext<TAggregate> CreateCommandExecutionContext(TAggregate aggregate);
    TAggregate ApplyEvent(TAggregate aggregate, object @event);
}