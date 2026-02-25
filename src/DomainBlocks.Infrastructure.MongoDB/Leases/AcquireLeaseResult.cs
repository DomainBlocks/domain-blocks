using System.Diagnostics.CodeAnalysis;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public sealed class AcquireLeaseResult<THandle> where THandle : class, ILeaseHandle
{
    public static readonly AcquireLeaseResult<THandle> NotAcquired = new(null, null);

    internal AcquireLeaseResult(THandle? handle, ILeaseSnapshot? initialSnapshot)
    {
        Handle = handle;
        InitialSnapshot = initialSnapshot;
    }

    [MemberNotNullWhen(true, nameof(Handle))]
    [MemberNotNullWhen(true, nameof(InitialSnapshot))]
    public bool IsAcquired => Handle is not null;

    public THandle? Handle { get; }

    public ILeaseSnapshot? InitialSnapshot { get; }
}