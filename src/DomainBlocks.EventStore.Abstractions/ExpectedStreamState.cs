namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Provides factory methods for creating expected event stream states.
/// </summary>
public static class ExpectedStreamState
{
    /// <summary>
    /// Creates an expectation that imposes no constraint on the stream state.
    /// </summary>
    /// <typeparam name="TVersion">The type used to represent stream versions.</typeparam>
    /// <returns>An expectation that matches any stream state.</returns>
    public static ExpectedStreamState<TVersion> Any<TVersion>() where TVersion : notnull =>
        ExpectedStreamState<TVersion>.Any;

    /// <summary>
    /// Creates an expectation that the stream does not exist.
    /// </summary>
    /// <typeparam name="TVersion">The type used to represent stream versions.</typeparam>
    /// <returns>An expectation that matches only a nonexistent stream.</returns>
    public static ExpectedStreamState<TVersion> DoesNotExist<TVersion>() where TVersion : notnull =>
        ExpectedStreamState<TVersion>.DoesNotExist;

    /// <summary>
    /// Creates an expectation that the stream exists, regardless of its version.
    /// </summary>
    /// <typeparam name="TVersion">The type used to represent stream versions.</typeparam>
    /// <returns>An expectation that matches any existing stream.</returns>
    public static ExpectedStreamState<TVersion> Exists<TVersion>() where TVersion : notnull =>
        ExpectedStreamState<TVersion>.Exists;

    /// <summary>
    /// Creates an expectation that the stream exists at the specified version.
    /// </summary>
    /// <typeparam name="TVersion">The type used to represent stream versions.</typeparam>
    /// <param name="version">The required stream version.</param>
    /// <returns>An expectation for the specified stream version.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="version"/> is <see langword="null"/>.</exception>
    public static ExpectedStreamState<TVersion> AtVersion<TVersion>(TVersion version) where TVersion : notnull =>
        ExpectedStreamState<TVersion>.AtVersion(version);
}

/// <summary>
/// Represents an expected state for an event stream operation.
/// </summary>
/// <typeparam name="TVersion">The type used to represent stream versions.</typeparam>
public readonly record struct ExpectedStreamState<TVersion> where TVersion : notnull
{
    /// <summary>
    /// An unconstrained expectation; the stream may exist at any version or may not exist.
    /// </summary>
    public static readonly ExpectedStreamState<TVersion> Any = new(ExpectedStreamStateKind.Any);

    /// <summary>
    /// An expectation that the stream does not exist.
    /// </summary>
    public static readonly ExpectedStreamState<TVersion> DoesNotExist = new(ExpectedStreamStateKind.DoesNotExist);

    /// <summary>
    /// An expectation that the stream exists at any version.
    /// </summary>
    public static readonly ExpectedStreamState<TVersion> Exists = new(ExpectedStreamStateKind.Exists);

    private ExpectedStreamState(ExpectedStreamStateKind kind, TVersion? version = default)
    {
        Kind = kind;
        Version = version;
    }

    /// <summary>
    /// Gets the kind of this expected stream state.
    /// </summary>
    public ExpectedStreamStateKind Kind { get; }

    /// <summary>
    /// Gets a value indicating whether a specific stream version is expected.
    /// </summary>
    public bool HasVersion => Kind == ExpectedStreamStateKind.AtVersion;

    /// <summary>
    /// Gets the expected stream version.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="HasVersion"/> is <see langword="false"/>.
    /// </exception>
    public TVersion Version => HasVersion
        ? field!
        : throw new InvalidOperationException("Expected stream state has no version.");

    /// <summary>
    /// Creates an expected stream state for a specific stream version.
    /// </summary>
    /// <param name="version">The required stream version.</param>
    /// <returns>An expectation for the specified stream version.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="version"/> is <see langword="null"/>.</exception>
    public static ExpectedStreamState<TVersion> AtVersion(TVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new ExpectedStreamState<TVersion>(ExpectedStreamStateKind.AtVersion, version);
    }

    /// <summary>
    /// Determines whether this expected state matches an observed stream state.
    /// </summary>
    /// <param name="observedState">The observed stream state to compare with this expectation.</param>
    /// <returns>
    /// <see langword="true"/> if the observed state satisfies this expectation; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Matches(ObservedStreamState<TVersion> observedState)
    {
        return Kind switch
        {
            ExpectedStreamStateKind.Any => true,
            ExpectedStreamStateKind.DoesNotExist => observedState.Kind == ObservedStreamStateKind.DoesNotExist,
            ExpectedStreamStateKind.Exists => observedState.HasVersion,
            ExpectedStreamStateKind.AtVersion =>
                observedState.HasVersion && EqualityComparer<TVersion>.Default.Equals(Version, observedState.Version),
            _ => false // Defensive fallback: kind not recognized
        };
    }

    /// <summary>
    /// Returns a string representation of this expected stream state.
    /// </summary>
    public override string ToString() => HasVersion ? $"Version={Version}" : Kind.ToString();
}