using DomainBlocks.Infrastructure.MongoDB.Utilities;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public interface ILeaseHandle : IAsyncDisposable
{
    LeaseClaim Claim { get; }
    ILeaseSnapshot CurrentSnapshot { get; }
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