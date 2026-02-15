namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public interface ILeaseHandle : IAsyncDisposable
{
    LeaseToken Token { get; }

    int ContentionPriority { get; }

    DateTimeOffset UpdatedAt { get; }

    DateTimeOffset HeldSince { get; }

    DateTimeOffset ExpiresAt { get; }

    CancellationToken LeaseLostToken { get; }

    Task<LeaseLostInfo> LeaseLostTask { get; }

    void ScheduleContentionPriorityChange(int priority);
}