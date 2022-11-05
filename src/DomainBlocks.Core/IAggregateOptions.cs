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
    Func<TAggregate, object, TAggregate> EventApplier { get; }

    TAggregate CreateNew();
    string MakeStreamKey(string id);
    string MakeSnapshotKey(string id);
    string MakeSnapshotKey(TAggregate aggregate);
    ICommandExecutionContext<TAggregate> CreateCommandExecutionContext(TAggregate aggregate);
}