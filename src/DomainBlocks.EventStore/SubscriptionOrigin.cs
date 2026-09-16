namespace DomainBlocks.EventStore;

/// <summary>
/// Provides subscription origins. <see cref="Start"/> and <see cref="End"/> convert implicitly to a
/// <see cref="SubscriptionOrigin{TPos}"/> of any position type, so the type argument need not be written at the call
/// site.
/// </summary>
public static class SubscriptionOrigin
{
    /// <summary>
    /// The start of an event sequence.
    /// </summary>
    public static StartMarker Start { get; } = new();

    /// <summary>
    /// The end of an event sequence.
    /// </summary>
    public static EndMarker End { get; } = new();

    /// <summary>
    /// Creates an origin representing the point immediately after a specific position within an event sequence.
    /// </summary>
    /// <typeparam name="TPos">The type used to represent positions.</typeparam>
    /// <param name="position">The position after which to begin receiving events.</param>
    /// <returns>An origin representing the point immediately after <paramref name="position"/>.</returns>
    public static SubscriptionOrigin<TPos>.After After<TPos>(TPos position) where TPos : notnull => new(position);

    /// <summary>
    /// A position-type-agnostic marker for the start of an event sequence. Converts implicitly to
    /// <see cref="SubscriptionOrigin{TPos}.Start"/>.
    /// </summary>
    public sealed class StartMarker
    {
        internal StartMarker()
        {
        }
    }

    /// <summary>
    /// A position-type-agnostic marker for the end of an event sequence. Converts implicitly to
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
/// Represents a subscription’s starting point within an event sequence, i.e., an individual event stream or the
/// global event log.
/// </summary>
/// <typeparam name="TPos">The type used to represent positions within the event sequence.</typeparam>
public abstract record SubscriptionOrigin<TPos> where TPos : notnull
{
    public static implicit operator SubscriptionOrigin<TPos>(SubscriptionOrigin.StartMarker _) => Start.Instance;

    public static implicit operator SubscriptionOrigin<TPos>(SubscriptionOrigin.EndMarker _) => End.Instance;

    /// <summary>
    /// Represents the start of an event sequence.
    /// </summary>
    public sealed record Start : SubscriptionOrigin<TPos>
    {
        public static readonly Start Instance = new();

        private Start()
        {
        }
    }

    /// <summary>
    /// Represents the end of an event sequence.
    /// </summary>
    public sealed record End : SubscriptionOrigin<TPos>
    {
        public static readonly End Instance = new();

        private End()
        {
        }
    }

    /// <summary>
    /// Represents a position after which to begin receiving events.
    /// </summary>
    /// <param name="Position">The position after which to begin receiving events.</param>
    public sealed record After(TPos Position) : SubscriptionOrigin<TPos>;
}
