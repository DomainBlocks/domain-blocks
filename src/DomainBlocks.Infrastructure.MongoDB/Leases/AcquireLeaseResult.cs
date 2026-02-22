using System.Diagnostics.CodeAnalysis;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public sealed class AcquireLeaseResult<THandle> where THandle : class, ILeaseHandle
{
    public static readonly AcquireLeaseResult<THandle> NotAcquired = new(null);

    internal AcquireLeaseResult(THandle? handle)
    {
        Handle = handle;
    }

    [MemberNotNullWhen(true, nameof(Handle))]
    public bool IsAcquired => Handle is not null;

    public THandle? Handle { get; }
}