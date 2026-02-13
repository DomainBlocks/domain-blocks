using DomainBlocks.Infrastructure.MongoDB.Sequences;
using MongoDB.Driver;

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

    Task<bool> TryFenceAsync(IClientSessionHandle session, CancellationToken cancellationToken = default);

    Task<SequenceRange?> NextSequenceRangeAsync(
        long count,
        CancellationToken cancellationToken = default);

    Task<SequenceRange?> NextSequenceRangeAsync(
        IClientSessionHandle session,
        long count,
        CancellationToken cancellationToken = default);

    void ScheduleContentionPriorityChange(int priority);
}