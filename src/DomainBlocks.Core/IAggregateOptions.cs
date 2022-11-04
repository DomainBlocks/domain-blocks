using System;
using System.Collections.Generic;

namespace DomainBlocks.Core;

public interface IAggregateOptions
{
    Type ClrType { get; }
    Type EventBaseType { get; }
    IEnumerable<IEventOptions> EventsOptions { get; }
}

public interface IAggregateOptions<TAggregate> : IAggregateOptions
{
    Func<TAggregate> Factory { get; }
    Func<TAggregate, string> IdSelector { get; }
    Func<string, string> IdToStreamKeySelector { get; }
    Func<string, string> IdToSnapshotKeySelector { get; }
    Func<TAggregate, object, TAggregate> EventApplier { get; }

    string SelectStreamKey(TAggregate aggregate);
    string SelectSnapshotKey(TAggregate aggregate);

    ICommandExecutionContext<TAggregate> CreateCommandExecutionContext(TAggregate aggregate);
}