namespace DomainBlocks.EventStore;

/// <summary>
/// Provides methods for creating <see cref="ObservedStreamState{TVersion}"/> values.
/// </summary>
public static class ObservedStreamState
{
    /// <summary>
    /// Returns the state of a stream that does not exist.
    /// </summary>
    /// <typeparam name="TVersion">The type of the stream version.</typeparam>
    /// <returns><see cref="ObservedStreamState{TVersion}.DoesNotExist"/>.</returns>
    public static ObservedStreamState<TVersion> DoesNotExist<TVersion>() where TVersion : notnull =>
        ObservedStreamState<TVersion>.DoesNotExist;

    /// <summary>
    /// Creates the state of a stream that exists at the specified version.
    /// </summary>
    /// <typeparam name="TVersion">The type of the stream version.</typeparam>
    /// <param name="version">The observed version.</param>
    /// <returns>The state of a stream at <paramref name="version"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="version"/> is <see langword="null"/>.</exception>
    public static ObservedStreamState<TVersion> AtVersion<TVersion>(TVersion version) where TVersion : notnull =>
        ObservedStreamState<TVersion>.AtVersion(version);
}

/// <summary>
/// Represents the state of a stream as observed by an operation. The default value is <see cref="DoesNotExist"/>.
/// </summary>
/// <typeparam name="TVersion">The type of the stream version.</typeparam>
public readonly record struct ObservedStreamState<TVersion> where TVersion : notnull
{
    /// <summary>
    /// The state of a stream that does not exist.
    /// </summary>
    public static readonly ObservedStreamState<TVersion> DoesNotExist = new(ObservedStreamStateKind.DoesNotExist);

    private ObservedStreamState(ObservedStreamStateKind kind, TVersion? version = default)
    {
        Kind = kind;
        Version = version;
    }

    /// <summary>
    /// Gets the kind of state.
    /// </summary>
    public ObservedStreamStateKind Kind { get; }

    /// <summary>
    /// Gets a value that indicates whether the stream exists.
    /// </summary>
    public bool HasVersion => Kind == ObservedStreamStateKind.AtVersion;

    /// <summary>
    /// Gets the observed version.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="HasVersion"/> is <see langword="false"/>.
    /// </exception>
    public TVersion Version => HasVersion
        ? field!
        : throw new InvalidOperationException("Stream state has no version.");

    /// <summary>
    /// Creates the state of a stream that exists at the specified version.
    /// </summary>
    /// <param name="version">The observed version.</param>
    /// <returns>The state of a stream at <paramref name="version"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="version"/> is <see langword="null"/>.</exception>
    public static ObservedStreamState<TVersion> AtVersion(TVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new ObservedStreamState<TVersion>(ObservedStreamStateKind.AtVersion, version);
    }

    /// <summary>
    /// Returns a string that represents this state.
    /// </summary>
    public override string ToString() => HasVersion ? $"Version={Version}" : Kind.ToString();
}