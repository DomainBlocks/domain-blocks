namespace DomainBlocks.EventStore.MongoDB.Appender.Coordination;

public sealed class RenewLeaseOptions
{
    public static readonly RenewLeaseOptions Default = new();

    public int? ContentionPriority { get; init; }

    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(30);
}