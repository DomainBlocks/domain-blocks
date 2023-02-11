namespace DomainBlocks.Core;

public interface IAggregateType
{
    Type ClrType { get; }
    IEnumerable<IEventType> EventTypes { get; }
}

public interface IAggregateType<TAggregate> : IAggregateType
{
    TAggregate CreateNew();
    string MakeStreamKey(string id);
    string MakeSnapshotKey(string id);
    string MakeSnapshotKey(TAggregate aggregate);
    ICommandExecutionContext<TAggregate> CreateCommandExecutionContext(TAggregate aggregate);
    TAggregate InvokeEventApplier(TAggregate aggregate, object @event);
}