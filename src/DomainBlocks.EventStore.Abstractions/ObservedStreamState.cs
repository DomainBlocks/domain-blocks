namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Provides factory methods for creating observed event stream states.
/// </summary>
public static class ObservedStreamState
{
    /// <summary>
    /// Creates an observed state indicating that the stream does not exist.
    /// </summary>
    /// <typeparam name="TVersion">The type used to represent stream versions.</typeparam>
    /// <returns>An observed state indicating that the stream does not exist.</returns>
    public static ObservedStreamState<TVersion> DoesNotExist<TVersion>() where TVersion : notnull =>
        ObservedStreamState<TVersion>.DoesNotExist;

    /// <summary>
    /// Creates an observed state indicating that the stream exists at the specified version.
    /// </summary>
    /// <typeparam name="TVersion">The type used to represent stream versions.</typeparam>
    /// <param name="version">The observed stream version.</param>
    /// <returns>An observed state for the specified stream version.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="version"/> is <see langword="null"/>.</exception>
    public static ObservedStreamState<TVersion> AtVersion<TVersion>(TVersion version) where TVersion : notnull =>
        ObservedStreamState<TVersion>.AtVersion(version);
}

/// <summary>
/// Represents the observed state of an event stream at a point in time. The default value represents a stream that
/// does not exist.
/// </summary>
/// <typeparam name="TVersion">The type used to represent the stream version.</typeparam>
public readonly record struct ObservedStreamState<TVersion> where TVersion : notnull
{
    /// <summary>
    /// Represents a stream with no events.
    /// </summary>
    public static readonly ObservedStreamState<TVersion> DoesNotExist = new(ObservedStreamStateKind.DoesNotExist);

    private ObservedStreamState(ObservedStreamStateKind kind, TVersion? version = default)
    {
        Kind = kind;
        Version = version;
    }

    /// <summary>
    /// Gets the kind of this observed stream state.
    /// </summary>
    public ObservedStreamStateKind Kind { get; }

    /// <summary>
    /// Gets a value indicating whether the stream exists at a specific version.
    /// </summary>
    public bool HasVersion => Kind == ObservedStreamStateKind.AtVersion;

    /// <summary>
    /// Gets the observed stream version.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="HasVersion"/> is <see langword="false"/>.
    /// </exception>
    public TVersion Version => HasVersion
        ? field!
        : throw new InvalidOperationException("Stream state has no version.");

    /// <summary>
    /// Creates an observed state representing an existing stream at the specified version.
    /// </summary>
    /// <param name="version">The observed stream version.</param>
    /// <returns>An observed state for the specified stream version.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="version"/> is <see langword="null"/>.</exception>
    public static ObservedStreamState<TVersion> AtVersion(TVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new ObservedStreamState<TVersion>(ObservedStreamStateKind.AtVersion, version);
    }

    /// <summary>
    /// Returns a string representation of this observed stream state.
    /// </summary>
    public override string ToString() => HasVersion ? $"Version={Version}" : Kind.ToString();
}