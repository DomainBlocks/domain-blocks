using System.Diagnostics.CodeAnalysis;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public sealed class LeaseWriteResult
{
    private LeaseWriteResult(bool isSuccess, ILeaseSnapshot? snapshot = null)
    {
        IsSuccess = isSuccess;
        Snapshot = snapshot;
    }

    [MemberNotNullWhen(true, nameof(Snapshot))]
    public bool IsSuccess { get; }

    public ILeaseSnapshot? Snapshot { get; }

    public static LeaseWriteResult Success(ILeaseSnapshot snapshot) => new(true, snapshot);

    public static LeaseWriteResult NotHeld() => new(false);
}