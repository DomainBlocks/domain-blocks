using DomainBlocks.Infrastructure.MongoDB.Leases;

namespace DomainBlocks.EventStore.MongoDB.Client.Appender.Events;

public sealed class LocalLeaseLost(LeaseClaim claim, LeaseLostInfo? lostInfo) : IAppenderEvent
{
    public LeaseClaim Claim { get; } = claim;
    public LeaseLostInfo? LostInfo { get; } = lostInfo;
}