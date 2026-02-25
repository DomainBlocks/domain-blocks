namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public static class LeaseSnapshotExtensions
{
    extension(ILeaseSnapshot snapshot)
    {
        public LeaseClaim Claim => new(snapshot.ResourceId, snapshot.HolderId, snapshot.Epoch);
    }
}