using System.Diagnostics.CodeAnalysis;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public sealed class LeaseAcquisition : IAsyncDisposable
{
    public static readonly LeaseAcquisition NotAcquired = new(null);

    private LeaseAcquisition(ILeaseHandle? handle)
    {
        Handle = handle;
    }

    public ILeaseHandle? Handle { get; }

    [MemberNotNullWhen(true, nameof(Handle))]
    public bool IsAcquired => Handle is not null;

    public static LeaseAcquisition From(ILeaseHandle handle) => new(handle);

    public ValueTask DisposeAsync() => Handle?.DisposeAsync() ?? ValueTask.CompletedTask;
}