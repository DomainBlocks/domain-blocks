namespace DomainBlocks.EventStore.MongoDB.Appender.Coordination;

public interface ILease : IAsyncDisposable
{
    string ResourceId { get; }

    string HolderId { get; }

    long Epoch { get; }

    int HolderPriority { get; set; }

    CancellationToken LeaseLostToken { get; }

    Task<LeaseLostInfo> LeaseLostTask { get; }
}