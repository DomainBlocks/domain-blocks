using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public interface ILeaseHandle : IAsyncDisposable
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

    Task<bool> TryFenceAsync(IClientSessionHandle session, CancellationToken cancellationToken = default);

    void ScheduleContentionPriorityChange(int priority);
}