namespace DomainBlocks.Coordination.MongoDB.Leases;

public sealed class RenewLeaseOptions
{
    public static readonly RenewLeaseOptions Default = new();

    public int? ContentionPriority { get; init; }

    public TimeSpan Duration { get; init; } = AcquireLeaseOptions.Default.Duration;

    public RenewLeaseOptions With(Action<Builder> configure)
    {
        var builder = new Builder(this);
        configure(builder);
        return builder.Build();
    }

    public sealed class Builder
    {
        internal Builder(RenewLeaseOptions options)
        {
            ContentionPriority = options.ContentionPriority;
            Duration = options.Duration;
        }

        public int? ContentionPriority { get; set; }

        public TimeSpan Duration { get; set; }

        internal RenewLeaseOptions Build() => new()
        {
            ContentionPriority = ContentionPriority,
            Duration = Duration
        };
    }
}