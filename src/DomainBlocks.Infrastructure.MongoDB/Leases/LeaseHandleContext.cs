using Microsoft.Extensions.Logging;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public sealed class LeaseHandleContext
{
    public required ILeaseSnapshot InitialSnapshot { get; init; }
    public required AcquireLeaseOptions AcquireOptions { get; init; }
    public required ILeaseStore LeaseStore { get; init; }
    public required ILogger Logger { get; init; }
    public required TimeProvider TimeProvider { get; init; }
}