namespace DomainBlocks.EventStore.Abstractions;

public static class ExpectedStreamState
{
    public static ExpectedStreamState<TVersion> Any<TVersion>() where TVersion : notnull =>
        ExpectedStreamState<TVersion>.Any;

    public static ExpectedStreamState<TVersion> DoesNotExist<TVersion>() where TVersion : notnull =>
        ExpectedStreamState<TVersion>.DoesNotExist;

    public static ExpectedStreamState<TVersion> Exists<TVersion>() where TVersion : notnull =>
        ExpectedStreamState<TVersion>.Exists;

    public static ExpectedStreamState<TVersion> AtVersion<TVersion>(TVersion version) where TVersion : notnull =>
        ExpectedStreamState<TVersion>.AtVersion(version);
}

/// <summary>
/// Represents the expected state of an event stream. Used to enforce concurrency or existence checks when performing
/// stream operations. The default value is <see cref="Any"/>.
/// </summary>
public readonly record struct ExpectedStreamState<TVersion> where TVersion : notnull
{
    /// <summary>
    /// Any state; stream may exist at any version or may not exist.
    /// </summary>
    public static readonly ExpectedStreamState<TVersion> Any = new(ExpectedStreamStateKind.Any);

    /// <summary>
    /// Stream must not exist.
    /// </summary>
    public static readonly ExpectedStreamState<TVersion> DoesNotExist = new(ExpectedStreamStateKind.DoesNotExist);

    /// <summary>
    /// Stream must exist.
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

    public bool HasVersion => Kind == ExpectedStreamStateKind.AtVersion;

    /// <summary>
    /// The expected stream version.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Kind"/> is not <see cref="ExpectedStreamStateKind.AtVersion"/>.
    /// </exception>
    public TVersion Version => HasVersion
        ? field!
        : throw new InvalidOperationException("Expected stream state has no version.");

    /// <summary>
    /// Creates an expected stream state for a specific version.
    /// </summary>
    public static ExpectedStreamState<TVersion> AtVersion(TVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new ExpectedStreamState<TVersion>(ExpectedStreamStateKind.AtVersion, version);
    }

    /// <summary>
    /// Returns <c>true</c> if this expected state matches the given actual state.
    /// </summary>
    public bool Matches(StreamState<TVersion> actualState)
    {
        return Kind switch
        {
            ExpectedStreamStateKind.Any => true,
            ExpectedStreamStateKind.DoesNotExist => actualState.Kind == StreamStateKind.DoesNotExist,
            ExpectedStreamStateKind.Exists => actualState.HasVersion,
            ExpectedStreamStateKind.AtVersion =>
                actualState.HasVersion && EqualityComparer<TVersion>.Default.Equals(Version, actualState.Version),
            _ => false // Defensive fallback: kind not recognized
        };
    }

    /// <summary>
    /// Returns a string representation of this expected stream state.
    /// </summary>
    public override string ToString() => HasVersion ? $"Version={Version}" : Kind.ToString();
}