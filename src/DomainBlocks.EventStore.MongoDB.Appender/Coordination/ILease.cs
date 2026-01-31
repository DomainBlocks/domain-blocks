namespace DomainBlocks.EventStore.MongoDB.Appender.Coordination;

public interface ILease : IAsyncDisposable
{
    string ResourceId { get; }

    string HolderId { get; }

    long Epoch { get; }

    int HolderPriority { get; }

    DateTime UpdatedAtUtc { get; }

    DateTime HeldSinceUtc { get; }

    DateTime ExpiresAtUtc { get; }

    CancellationToken LeaseLostToken { get; }

    Task<LeaseLostInfo> LeaseLostTask { get; }

    void ScheduleHolderPriorityChange(int priority);
}