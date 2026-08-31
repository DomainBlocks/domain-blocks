namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Provides factory methods for creating subscription origins.
/// </summary>
public static class SubscriptionOrigin
{
    /// <summary>
    /// Creates an origin representing the start of an event sequence.
    /// </summary>
    /// <typeparam name="TPos">The type used to represent positions.</typeparam>
    /// <returns>An origin representing the start.</returns>
    public static SubscriptionOrigin<TPos>.Start Start<TPos>() where TPos : notnull =>
        SubscriptionOrigin<TPos>.Start.Instance;

    /// <summary>
    /// Creates an origin representing the end of an event sequence.
    /// </summary>
    /// <typeparam name="TPos">The type used to represent positions.</typeparam>
    /// <returns>An origin representing the end.</returns>
    public static SubscriptionOrigin<TPos>.End End<TPos>() where TPos : notnull =>
        SubscriptionOrigin<TPos>.End.Instance;

    /// <summary>
    /// Creates an origin representing the point immediately after a specific position within an event sequence.
    /// </summary>
    /// <typeparam name="TPos">The type used to represent positions.</typeparam>
    /// <param name="position">The position after which to begin receiving events.</param>
    /// <returns>An origin representing the point immediately after <paramref name="position"/>.</returns>
    public static SubscriptionOrigin<TPos>.After After<TPos>(TPos position) where TPos : notnull => new(position);
}

/// <summary>
/// Represents a subscription’s starting point within an event sequence, i.e., an individual event stream or the global
/// event log.
/// </summary>
/// <typeparam name="TPos">The type used to represent positions within the event sequence.</typeparam>
public abstract record SubscriptionOrigin<TPos> where TPos : notnull
{
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