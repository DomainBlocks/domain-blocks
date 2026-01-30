namespace DomainBlocks.EventStore.MongoDB.Appender.Coordination;

public sealed class AcquireLeaseOptions
{
    public static readonly AcquireLeaseOptions Default = new();

    public string HolderIdPrefix { get; init; } = Environment.MachineName;

    public int HolderPriority { get; init; }

    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan MinHolderAge { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan PreemptWindow { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan AcquireTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan AcquireRetryDelay { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan RenewInterval { get; init; } = TimeSpan.FromSeconds(10);
}