using System;
using System.Collections.Generic;

namespace DomainBlocks.Core;

public interface IAggregateOptions
{
    public Type ClrType { get; }
    public Type EventBaseType { get; }
    public IEnumerable<IEventOptions> EventsOptions { get; }
}

public interface IAggregateOptions<TAggregate> : IAggregateOptions
{
    public Func<TAggregate> Factory { get; }
    public Func<TAggregate, string> IdSelector { get; }
    public Func<string, string> IdToStreamKeySelector { get; }
    public Func<string, string> IdToSnapshotKeySelector { get; }
    public Func<TAggregate, object, TAggregate> EventApplier { get; }

    public string SelectStreamKey(TAggregate aggregate);
    public string SelectSnapshotKey(TAggregate aggregate);

    public ICommandExecutionContext<TAggregate> CreateCommandExecutionContext(TAggregate aggregate);
}