namespace DomainBlocks.Core;

public interface IEventSourcedEntityType
{
    Type ClrType { get; }
    IEnumerable<IEventType> EventTypes { get; }

    string MakeStreamKey(string id);
    string MakeSnapshotKey(string id);
}

public interface IEventSourcedEntityType<TEntity> : IEventSourcedEntityType
{
    TEntity CreateNew();
    string MakeSnapshotKey(TEntity entity);
}