namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public interface ILeaseHandle : IAsyncDisposable
{
    LeaseClaim Claim { get; }

    int ContentionPriority { get; }

    DateTimeOffset HeldSince { get; }

    DateTimeOffset ExpiresAt { get; }

    DateTimeOffset UpdatedAt { get; }

    CancellationToken LeaseLostToken { get; }

    Task<LeaseLostInfo> LeaseLostTask { get; }

    void ScheduleContentionPriorityChange(int priority);
}

public interface ILeaseHandle<TState> : ILeaseHandle
{
    Task<bool> TryUpdateStateAsync(
        Action<IScopedUpdateBuilder<TState>> updateState,
        CancellationToken cancellationToken = default);
}