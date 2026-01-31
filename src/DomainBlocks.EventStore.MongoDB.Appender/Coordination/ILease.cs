namespace DomainBlocks.EventStore.MongoDB.Appender.Coordination;

public interface ILease : IAsyncDisposable
{
    string ResourceId { get; }

    string HolderId { get; }

    long Epoch { get; }

    int ContentionPriority { get; }

    DateTimeOffset UpdatedAt { get; }

    DateTimeOffset HeldSince { get; }

    DateTimeOffset ExpiresAt { get; }

    CancellationToken LeaseLostToken { get; }

    Task<LeaseLostInfo> LeaseLostTask { get; }

    void ScheduleContentionPriorityChange(int priority);
}