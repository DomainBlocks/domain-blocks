namespace DomainBlocks.Core;

public interface IEntityType
{
    Type ClrType { get; }
    IEnumerable<IEventOptions> EventsOptions { get; }

    string MakeStreamKey(string id);
    string MakeSnapshotKey(string id);
}

public interface IEntityType<TEntity> : IEntityType
{
    TEntity CreateNew();
    string MakeSnapshotKey(TEntity aggregate);
}