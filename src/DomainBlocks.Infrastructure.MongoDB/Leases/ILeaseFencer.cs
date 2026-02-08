using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public interface ILeaseFencer
{
    Task<bool> TryFenceAsync(
        IClientSessionHandle session,
        string resourceId,
        string holderId,
        long epoch,
        CancellationToken cancellationToken = default);
}