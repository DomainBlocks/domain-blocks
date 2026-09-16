namespace DomainBlocks.EventStore;

/// <summary>
/// Provides read origins. <see cref="Start"/> and <see cref="End"/> convert implicitly to a
/// <see cref="ReadOrigin{TPos}"/> of any position type, so the type argument need not be written at the call site.
/// </summary>
public static class ReadOrigin
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
    /// Creates an origin representing a specific position within an event sequence.
    /// </summary>
    /// <typeparam name="TPos">The type used to represent positions.</typeparam>
    /// <param name="position">The position at which to begin reading.</param>
    /// <returns>An origin representing <paramref name="position"/>.</returns>
    public static ReadOrigin<TPos>.At At<TPos>(TPos position) where TPos : notnull => new(position);

    /// <summary>
    /// A position-type-agnostic marker for the start of an event sequence. Converts implicitly to
    /// <see cref="ReadOrigin{TPos}.Start"/>.
    /// </summary>
    public sealed class StartMarker
    {
        internal StartMarker()
        {
        }
    }

    /// <summary>
    /// A position-type-agnostic marker for the end of an event sequence. Converts implicitly to
    /// <see cref="ReadOrigin{TPos}.End"/>.
    /// </summary>
    public sealed class EndMarker
    {
        internal EndMarker()
        {
        }
    }
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

    public static implicit operator ReadOrigin<TPos>(ReadOrigin.StartMarker _) => Start.Instance;

    public static implicit operator ReadOrigin<TPos>(ReadOrigin.EndMarker _) => End.Instance;
}