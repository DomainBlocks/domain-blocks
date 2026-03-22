namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public interface IAppendRequestCompleter
{
    void Complete(IReadOnlyCollection<Guid> commitIds);
}