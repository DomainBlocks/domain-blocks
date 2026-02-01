namespace DomainBlocks.Coordination.MongoDB.Leases;

public sealed class AcquireLeaseOptions
{
    public static readonly AcquireLeaseOptions Default = new();

    public string HolderIdPrefix { get; init; } = Environment.MachineName;

    public int ContentionPriority { get; init; }

    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan MinTenure { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan AcquireTimeout { get; init; } = TimeSpan.FromSeconds(45);

    public TimeSpan AcquireRetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan RenewInterval { get; init; } = TimeSpan.FromSeconds(10);

    public AcquireLeaseOptions With(Action<Builder> configure)
    {
        var builder = new Builder(this);
        configure(builder);
        return builder.Build();
    }

    public sealed class Builder
    {
        internal Builder(AcquireLeaseOptions options)
        {
            HolderIdPrefix = options.HolderIdPrefix;
            ContentionPriority = options.ContentionPriority;
            Duration = options.Duration;
            MinTenure = options.MinTenure;
            AcquireTimeout = options.AcquireTimeout;
            AcquireRetryDelay = options.AcquireRetryDelay;
            RenewInterval = options.RenewInterval;
        }

        public string HolderIdPrefix { get; set; }

        public int ContentionPriority { get; set; }

        public TimeSpan Duration { get; set; }

        public TimeSpan MinTenure { get; set; }

        public TimeSpan AcquireTimeout { get; set; }

        public TimeSpan AcquireRetryDelay { get; set; }

        public TimeSpan RenewInterval { get; set; }

        internal AcquireLeaseOptions Build() => new()
        {
            HolderIdPrefix = HolderIdPrefix,
            ContentionPriority = ContentionPriority,
            Duration = Duration,
            MinTenure = MinTenure,
            AcquireTimeout = AcquireTimeout,
            AcquireRetryDelay = AcquireRetryDelay,
            RenewInterval = RenewInterval
        };
    }
}