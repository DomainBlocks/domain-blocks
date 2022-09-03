namespace DomainBlocks.Persistence.New;

public interface IAggregateType<TAggregate> : IAggregateType
{
    public TAggregate CreateNew();
    public string SelectId(TAggregate aggregate);
    public string SelectStreamKey(TAggregate aggregate);
    public string SelectSnapshotKey(TAggregate aggregate);

    public TAggregate ApplyEvent(TAggregate aggregate, object @event);
    public ICommandExecutionContext<TAggregate> GetCommandExecutionContext(TAggregate aggregate);
}