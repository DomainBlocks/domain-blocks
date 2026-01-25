using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Represents the expected state of an event stream. Used to enforce concurrency or existence checks when performing
/// stream operations. The default value is <see cref="Any"/>.
/// </summary>
public readonly record struct ExpectedStreamState
{
    private const string VersionPrefix = "Version=";

    /// <summary>
    /// Any state; stream may exist at any version or may not exist.
    /// </summary>
    public static readonly ExpectedStreamState Any = new(ExpectedStreamStateKind.Any);

    /// <summary>
    /// Stream must exist.
    /// </summary>
    public static readonly ExpectedStreamState StreamExists = new(ExpectedStreamStateKind.StreamExists);

    /// <summary>
    /// Stream must not exist.
    /// </summary>
    public static readonly ExpectedStreamState StreamDoesNotExist = new(ExpectedStreamStateKind.StreamDoesNotExist);

    private ExpectedStreamState(ExpectedStreamStateKind kind, StreamVersion? version = null)
    {
        Kind = kind;
        Version = version;
    }

    /// <summary>
    /// Gets the kind of this expected stream state.
    /// </summary>
    public ExpectedStreamStateKind Kind { get; }

    /// <summary>
    /// The expected stream version when <see cref="IsSpecificVersion"/> is <c>true</c>, otherwise <c>null</c>.
    /// </summary>
    public StreamVersion? Version { get; }

    /// <summary>
    /// True if this instance is <see cref="Any"/>.
    /// </summary>
    public bool IsAny => Kind == ExpectedStreamStateKind.Any;

    /// <summary>
    /// True if this instance is <see cref="StreamDoesNotExist"/>.
    /// </summary>
    public bool IsStreamDoesNotExist => Kind == ExpectedStreamStateKind.StreamDoesNotExist;

    /// <summary>
    /// True if this instance is <see cref="StreamExists"/>.
    /// </summary>
    public bool IsStreamExists => Kind == ExpectedStreamStateKind.StreamExists;

    /// <summary>
    /// True if this instance represents a specific stream version.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Version))]
    public bool IsSpecificVersion => Kind == ExpectedStreamStateKind.SpecificVersion;

    /// <summary>
    /// Creates an expected stream state for a specific version.
    /// </summary>
    public static ExpectedStreamState SpecificVersion(StreamVersion version)
    {
        return new ExpectedStreamState(ExpectedStreamStateKind.SpecificVersion, version);
    }

    public static bool TryParse(string input, [NotNullWhen(true)] out ExpectedStreamState? result)
    {
        result = null;

        if (string.Equals(input, Any.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            result = Any;
        }
        else if (string.Equals(input, StreamDoesNotExist.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            result = StreamDoesNotExist;
        }
        else if (string.Equals(input, StreamExists.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            result = StreamExists;
        }
        else if (input.StartsWith(VersionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var raw = input[VersionPrefix.Length..];

            if (ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                result = SpecificVersion(new StreamVersion(v));
        }

        return result.HasValue;
    }

    /// <summary>
    /// Returns <c>true</c> if this expected state matches the given actual state.
    /// </summary>
    public bool Matches(StreamState actualState)
    {
        if (IsAny)
            return true;

        if (IsStreamDoesNotExist)
            return actualState.IsStreamDoesNotExist;

        if (IsStreamExists)
            return actualState.IsStreamExists;

        if (IsSpecificVersion)
            return actualState.IsStreamExists && Version.Value == actualState.Version.Value;

        // Defensive fallback: kind not recognized
        return false;
    }

    /// <summary>
    /// Returns a string representation of this expected stream state.
    /// </summary>
    public override string ToString() => Kind switch
    {
        ExpectedStreamStateKind.Any => nameof(Any),
        ExpectedStreamStateKind.StreamDoesNotExist => nameof(StreamDoesNotExist),
        ExpectedStreamStateKind.StreamExists => nameof(StreamExists),
        _ => $"{VersionPrefix}{Version?.Value}"
    };
}