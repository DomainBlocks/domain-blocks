namespace DomainBlocks.EventStore;

/// <summary>
/// Provides subscription origins. <see cref="Start"/> and <see cref="End"/> convert implicitly to
/// <see cref="SubscriptionOrigin{TPos}"/> for any position type.
/// </summary>
public static class SubscriptionOrigin
{
    /// <summary>
    /// Gets the start of the stream or event log.
    /// </summary>
    public static StartMarker Start { get; } = new();

    /// <summary>
    /// Gets the end of the stream or event log.
    /// </summary>
    public static EndMarker End { get; } = new();

    /// <summary>
    /// Creates an origin just after the specified position.
    /// </summary>
    /// <typeparam name="TPos">The type of the position.</typeparam>
    /// <param name="position">The position after which to receive events.</param>
    /// <returns>An origin just after <paramref name="position"/>.</returns>
    public static SubscriptionOrigin<TPos>.After After<TPos>(TPos position) where TPos : notnull => new(position);

    /// <summary>
    /// Represents <see cref="Start"/> for any position type. Converts implicitly to
    /// <see cref="SubscriptionOrigin{TPos}.Start"/>.
    /// </summary>
    public sealed class StartMarker
    {
        internal StartMarker()
        {
        }
    }

    /// <summary>
    /// Represents <see cref="End"/> for any position type. Converts implicitly to
    /// <see cref="SubscriptionOrigin{TPos}.End"/>.
    /// </summary>
    public sealed class EndMarker
    {
        internal EndMarker()
        {
        }
    }
}

/// <summary>
/// Represents where a subscription starts in a stream or the event log.
/// </summary>
/// <typeparam name="TPos">The type of the position.</typeparam>
public abstract record SubscriptionOrigin<TPos> where TPos : notnull
{
    public static implicit operator SubscriptionOrigin<TPos>(SubscriptionOrigin.StartMarker _) => Start.Instance;

    public static implicit operator SubscriptionOrigin<TPos>(SubscriptionOrigin.EndMarker _) => End.Instance;

    /// <summary>
    /// Represents the start of the stream or event log.
    /// </summary>
    public sealed record Start : SubscriptionOrigin<TPos>
    {
        public static readonly Start Instance = new();

        private Start()
        {
        }
    }

    /// <summary>
    /// Represents the end of the stream or event log.
    /// </summary>
    public sealed record End : SubscriptionOrigin<TPos>
    {
        public static readonly End Instance = new();

        private End()
        {
        }
    }

    /// <summary>
    /// Represents the point just after a position.
    /// </summary>
    /// <param name="Position">The position after which to receive events.</param>
    public sealed record After(TPos Position) : SubscriptionOrigin<TPos>;
}
