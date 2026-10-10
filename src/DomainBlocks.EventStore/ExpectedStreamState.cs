namespace DomainBlocks.EventStore;

/// <summary>
/// Provides methods for creating <see cref="ExpectedStreamState{TVersion}"/> values.
/// </summary>
public static class ExpectedStreamState
{
    /// <summary>
    /// Returns an expectation that any stream state satisfies.
    /// </summary>
    /// <typeparam name="TVersion">The type of the stream version.</typeparam>
    /// <returns><see cref="ExpectedStreamState{TVersion}.Any"/>.</returns>
    public static ExpectedStreamState<TVersion> Any<TVersion>() where TVersion : notnull =>
        ExpectedStreamState<TVersion>.Any;

    /// <summary>
    /// Returns an expectation that the stream does not exist.
    /// </summary>
    /// <typeparam name="TVersion">The type of the stream version.</typeparam>
    /// <returns><see cref="ExpectedStreamState{TVersion}.DoesNotExist"/>.</returns>
    public static ExpectedStreamState<TVersion> DoesNotExist<TVersion>() where TVersion : notnull =>
        ExpectedStreamState<TVersion>.DoesNotExist;

    /// <summary>
    /// Returns an expectation that the stream exists at any version.
    /// </summary>
    /// <typeparam name="TVersion">The type of the stream version.</typeparam>
    /// <returns><see cref="ExpectedStreamState{TVersion}.Exists"/>.</returns>
    public static ExpectedStreamState<TVersion> Exists<TVersion>() where TVersion : notnull =>
        ExpectedStreamState<TVersion>.Exists;

    /// <summary>
    /// Creates an expectation that the stream is at the specified version.
    /// </summary>
    /// <typeparam name="TVersion">The type of the stream version.</typeparam>
    /// <param name="version">The expected version.</param>
    /// <returns>An expectation that the stream is at <paramref name="version"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="version"/> is <see langword="null"/>.</exception>
    public static ExpectedStreamState<TVersion> AtVersion<TVersion>(TVersion version) where TVersion : notnull =>
        ExpectedStreamState<TVersion>.AtVersion(version);
}

/// <summary>
/// Represents the state a stream must be in for an append to succeed, for optimistic concurrency control.
/// </summary>
/// <typeparam name="TVersion">The type of the stream version.</typeparam>
public readonly record struct ExpectedStreamState<TVersion> where TVersion : notnull
{
    /// <summary>
    /// An expectation that any stream state satisfies.
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
    /// Gets the kind of expectation.
    /// </summary>
    public ExpectedStreamStateKind Kind { get; }

    /// <summary>
    /// Gets a value that indicates whether a specific version is expected.
    /// </summary>
    public bool HasVersion => Kind == ExpectedStreamStateKind.AtVersion;

    /// <summary>
    /// Gets the expected version.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="HasVersion"/> is <see langword="false"/>.
    /// </exception>
    public TVersion Version => HasVersion
        ? field!
        : throw new InvalidOperationException("Expected stream state has no version.");

    /// <summary>
    /// Creates an expectation that the stream is at the specified version.
    /// </summary>
    /// <param name="version">The expected version.</param>
    /// <returns>An expectation that the stream is at <paramref name="version"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="version"/> is <see langword="null"/>.</exception>
    public static ExpectedStreamState<TVersion> AtVersion(TVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new ExpectedStreamState<TVersion>(ExpectedStreamStateKind.AtVersion, version);
    }

    /// <summary>
    /// Determines whether an observed stream state satisfies this expectation.
    /// </summary>
    /// <param name="observedState">The observed stream state.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="observedState"/> satisfies this expectation; otherwise,
    /// <see langword="false"/>.
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
            _ => false
        };
    }

    /// <summary>
    /// Returns a string that represents this expectation.
    /// </summary>
    public override string ToString() => HasVersion ? $"Version={Version}" : Kind.ToString();
}