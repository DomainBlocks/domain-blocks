namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Provides factory methods for creating read origins.
/// </summary>
public static class ReadOrigin
{
    /// <summary>
    /// Creates an origin representing the start of an event sequence.
    /// </summary>
    /// <typeparam name="TPos">The type used to represent positions.</typeparam>
    /// <returns>An origin representing the start.</returns>
    public static ReadOrigin<TPos>.Start Start<TPos>() where TPos : notnull => ReadOrigin<TPos>.Start.Instance;

    /// <summary>
    /// Creates an origin representing the end of an event sequence.
    /// </summary>
    /// <typeparam name="TPos">The type used to represent positions.</typeparam>
    /// <returns>An origin representing the end.</returns>
    public static ReadOrigin<TPos>.End End<TPos>() where TPos : notnull => ReadOrigin<TPos>.End.Instance;

    /// <summary>
    /// Creates an origin representing a specific position within an event sequence.
    /// </summary>
    /// <typeparam name="TPos">The type used to represent positions.</typeparam>
    /// <param name="position">The position at which to begin reading.</param>
    /// <returns>An origin representing <paramref name="position"/>.</returns>
    public static ReadOrigin<TPos>.At At<TPos>(TPos position) where TPos : notnull => new(position);
}

/// <summary>
/// Represents a read operation's starting point within an event sequence, i.e., an individual event stream or the
/// global event log.
/// </summary>
/// <typeparam name="TPos"> The type used to represent positions within the event sequence. </typeparam>
public abstract record ReadOrigin<TPos> where TPos : notnull
{
    /// <summary>
    /// Represents the start of an event sequence.
    /// </summary>
    public sealed record Start : ReadOrigin<TPos>
    {
        public static readonly Start Instance = new();

        private Start()
        {
        }
    }

    /// <summary>
    /// Represents the end of an event sequence.
    /// </summary>
    public sealed record End : ReadOrigin<TPos>
    {
        public static readonly End Instance = new();

        private End()
        {
        }
    }

    /// <summary>
    /// Represents a position within an event sequence.
    /// </summary>
    /// <param name="Position">The position at which to begin reading.</param>
    public sealed record At(TPos Position) : ReadOrigin<TPos>;
}