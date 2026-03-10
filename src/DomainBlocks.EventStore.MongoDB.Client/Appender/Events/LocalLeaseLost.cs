using DomainBlocks.Infrastructure.MongoDB.Leases;

namespace DomainBlocks.EventStore.MongoDB.Client.Appender.Events;

public sealed class LocalLeaseLost(LeaseClaim leaseClaim, LeaseLostInfo leaseLostInfo) : IAppenderEvent
{
    public LeaseClaim LeaseClaim { get; } = leaseClaim;
    public LeaseLostInfo LeaseLostInfo { get; } = leaseLostInfo;
}