using System.Diagnostics;

namespace DomainBlocks.EventStore.Abstractions.New;

// public static class ExpectedStreamState2
// {
//     public static ExpectedStreamState2<TVersion> Any<TVersion>()
//         where TVersion : notnull =>
//         ExpectedStreamState2<TVersion>.Any.Instance;
//
//     public static ExpectedStreamState2<TVersion> DoesNotExist<TVersion>()
//         where TVersion : notnull =>
//         ExpectedStreamState2<TVersion>.DoesNotExist.Instance;
//
//     public static ExpectedStreamState2<TVersion> Exists<TVersion>()
//         where TVersion : notnull =>
//         ExpectedStreamState2<TVersion>.Exists.Instance;
//
//     public static ExpectedStreamState2<TVersion> AtVersion<TVersion>(TVersion version) where TVersion : notnull
//     {
//         ArgumentNullException.ThrowIfNull(version);
//         return new ExpectedStreamState2<TVersion>.AtVersion(version);
//     }
// }
//
// public abstract record ExpectedStreamState2<TVersion> where TVersion : notnull
// {
//     private ExpectedStreamState2()
//     {
//     }
//
//     public abstract bool Matches(StreamState2<TVersion> actualState);
//
//     public sealed record Any : ExpectedStreamState2<TVersion>
//     {
//         public static Any Instance { get; } = new();
//
//         private Any()
//         {
//         }
//
//         public override bool Matches(StreamState2<TVersion> actualState) => true;
//     }
//
//     public sealed record DoesNotExist : ExpectedStreamState2<TVersion>
//     {
//         public static DoesNotExist Instance { get; } = new();
//
//         private DoesNotExist()
//         {
//         }
//
//         public override bool Matches(StreamState2<TVersion> actualState) =>
//             actualState.Kind == StreamStateKind.DoesNotExist;
//     }
//
//     public sealed record Exists : ExpectedStreamState2<TVersion>
//     {
//         public static Exists Instance { get; } = new();
//
//         private Exists()
//         {
//         }
//
//         public override bool Matches(StreamState2<TVersion> actualState) =>
//             actualState.Kind == StreamStateKind.AtVersion;
//     }
//
//     public sealed record AtVersion(TVersion Version) : ExpectedStreamState2<TVersion>
//     {
//         public override bool Matches(StreamState2<TVersion> actualState) =>
//             actualState.Kind == StreamStateKind.AtVersion &&
//             EqualityComparer<TVersion>.Default.Equals(Version, actualState.Version);
//     }
// }

/// <summary>
/// Represents the expected state of an event stream. Used to enforce concurrency or existence checks when performing
/// stream operations. The default value is <see cref="Any"/>.
/// </summary>
public readonly record struct ExpectedStreamState2<TVersion> where TVersion : notnull
{
    private const string VersionPrefix = "Version=";

    /// <summary>
    /// Any state; stream may exist at any version or may not exist.
    /// </summary>
    public static readonly ExpectedStreamState2<TVersion> Any = new(ExpectedStreamStateKind.Any);

    /// <summary>
    /// Stream must not exist.
    /// </summary>
    public static readonly ExpectedStreamState2<TVersion> DoesNotExist = new(ExpectedStreamStateKind.DoesNotExist);

    /// <summary>
    /// Stream must exist.
    /// </summary>
    public static readonly ExpectedStreamState2<TVersion> Exists = new(ExpectedStreamStateKind.Exists);

    private readonly TVersion? _version;

    private ExpectedStreamState2(ExpectedStreamStateKind kind, TVersion? version = default)
    {
        Kind = kind;
        _version = version;
    }

    /// <summary>
    /// Gets the kind of this expected stream state.
    /// </summary>
    public ExpectedStreamStateKind Kind { get; }

    /// <summary>
    /// The expected stream version.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Kind"/> is not <see cref="ExpectedStreamStateKind.AtVersion"/>.
    /// </exception>
    public TVersion Version => Kind == ExpectedStreamStateKind.AtVersion
        ? _version!
        : throw new InvalidOperationException(
            $"Version is only available when Kind is '{nameof(ExpectedStreamStateKind.AtVersion)}'. Kind: '{Kind}'.");

    /// <summary>
    /// Creates an expected stream state for a specific version.
    /// </summary>
    public static ExpectedStreamState2<TVersion> AtVersion(TVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new ExpectedStreamState2<TVersion>(ExpectedStreamStateKind.AtVersion, version);
    }

    /// <summary>
    /// Returns <c>true</c> if this expected state matches the given actual state.
    /// </summary>
    public bool Matches(StreamState2<TVersion> actualState)
    {
        return Kind switch
        {
            ExpectedStreamStateKind.Any => true,
            ExpectedStreamStateKind.DoesNotExist => actualState.Kind == StreamStateKind.DoesNotExist,
            ExpectedStreamStateKind.Exists => actualState.Kind == StreamStateKind.AtVersion,
            ExpectedStreamStateKind.AtVersion =>
                actualState.Kind == StreamStateKind.AtVersion &&
                EqualityComparer<TVersion>.Default.Equals(_version!, actualState.Version),
            _ => false // Defensive fallback: kind not recognized
        };
    }

    /// <summary>
    /// Returns a string representation of this expected stream state.
    /// </summary>
    public override string ToString() => Kind switch
    {
        ExpectedStreamStateKind.Any => nameof(Any),
        ExpectedStreamStateKind.DoesNotExist => nameof(DoesNotExist),
        ExpectedStreamStateKind.Exists => nameof(Exists),
        ExpectedStreamStateKind.AtVersion => $"{VersionPrefix}{_version}",
        _ => throw new UnreachableException($"Unknown {nameof(ExpectedStreamStateKind)} '{Kind}'.")
    };
}