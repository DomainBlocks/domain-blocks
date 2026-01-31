namespace DomainBlocks.EventStore.MongoDB.Appender.Coordination;

public interface ILease : IAsyncDisposable
{
    string ResourceId { get; }

    string HolderId { get; }

    int HolderPriority { get; set; }

    long Epoch { get; }

    DateTime UpdatedAtUtc { get; }

    DateTime HeldSinceUtc { get; }

    DateTime ExpiresAtUtc { get; }

    CancellationToken LeaseLostToken { get; }

    Task<LeaseLostInfo> LeaseLostTask { get; }
}