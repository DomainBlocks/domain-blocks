namespace DomainBlocks.EventStore;

/// <summary>
/// Provides read origins. <see cref="Start"/> and <see cref="End"/> convert implicitly to
/// <see cref="ReadOrigin{TPos}"/> for any position type.
/// </summary>
public static class ReadOrigin
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
    /// Creates an origin at the specified position.
    /// </summary>
    /// <typeparam name="TPos">The type of the position.</typeparam>
    /// <param name="position">The position to start reading at.</param>
    /// <returns>An origin at <paramref name="position"/>.</returns>
    public static ReadOrigin<TPos>.At At<TPos>(TPos position) where TPos : notnull => new(position);

    /// <summary>
    /// Represents <see cref="Start"/> for any position type. Converts implicitly to
    /// <see cref="ReadOrigin{TPos}.Start"/>.
    /// </summary>
    public sealed class StartMarker
    {
        internal StartMarker()
        {
        }
    }

    /// <summary>
    /// Represents <see cref="End"/> for any position type. Converts implicitly to <see cref="ReadOrigin{TPos}.End"/>.
    /// </summary>
    public sealed class EndMarker
    {
        internal EndMarker()
        {
        }
    }
}

/// <summary>
/// Represents where a read starts in a stream or the event log.
/// </summary>
/// <typeparam name="TPos">The type of the position.</typeparam>
public abstract record ReadOrigin<TPos> where TPos : notnull
{
    /// <summary>
    /// Represents the start of the stream or event log.
    /// </summary>
    public sealed record Start : ReadOrigin<TPos>
    {
        public static readonly Start Instance = new();

        private Start()
        {
        }
    }

    /// <summary>
    /// Represents the end of the stream or event log.
    /// </summary>
    public sealed record End : ReadOrigin<TPos>
    {
        public static readonly End Instance = new();

        private End()
        {
        }
    }

    /// <summary>
    /// Represents a position to start reading at.
    /// </summary>
    /// <param name="Position">The position to start reading at.</param>
    public sealed record At(TPos Position) : ReadOrigin<TPos>;

    public static implicit operator ReadOrigin<TPos>(ReadOrigin.StartMarker _) => Start.Instance;

    public static implicit operator ReadOrigin<TPos>(ReadOrigin.EndMarker _) => End.Instance;
}