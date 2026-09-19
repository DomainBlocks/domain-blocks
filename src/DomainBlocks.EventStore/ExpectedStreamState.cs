using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace DomainBlocks.EventStore;

/// <summary>
/// Provides the expected event stream states that carry no version. Each converts implicitly to an
/// <see cref="ExpectedStreamState{TVersion}"/> of any version type, as a version itself does, so the type argument need
/// not be written at the call site. To expect nothing of the stream, omit the expected state.
/// </summary>
public static class ExpectedStreamState
{
    /// <summary>
    /// An expectation that the stream does not exist.
    /// </summary>
    public static StreamDoesNotExist DoesNotExist => default;

    /// <summary>
    /// An expectation that the stream exists at any version.
    /// </summary>
    public static StreamExists Exists => default;
}

/// <summary>
/// Represents an expected state for an event stream operation: that the stream does not exist, that it exists, or
/// that it exists at a version.
/// </summary>
/// <remarks>
/// A union of <see cref="StreamDoesNotExist"/>, <see cref="StreamExists"/> and <typeparamref name="TVersion"/>: each
/// converts implicitly to this type, and a <see langword="switch"/> over all three is exhaustive. The default value
/// expects nothing, so it matches any stream state; it is <see cref="Any"/>, and matches <see langword="null"/>.
/// </remarks>
/// <typeparam name="TVersion">The type used to represent stream versions.</typeparam>
[Union]
public readonly record struct ExpectedStreamState<TVersion> : IUnion where TVersion : notnull
{
    /// <summary>
    /// An unconstrained expectation; the stream may exist at any version or may not exist. This is the default value.
    /// </summary>
    public static readonly ExpectedStreamState<TVersion> Any = default;

    private readonly TVersion? _version;
    private readonly Case _case;

    public ExpectedStreamState(StreamDoesNotExist _)
    {
        _case = Case.DoesNotExist;
    }

    public ExpectedStreamState(StreamExists _)
    {
        _case = Case.Exists;
    }

    /// <exception cref="ArgumentNullException"><paramref name="version"/> is <see langword="null"/>.</exception>
    public ExpectedStreamState(TVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        _version = version;
        _case = Case.AtVersion;
    }

    private enum Case : byte
    {
        Any,
        DoesNotExist,
        Exists,
        AtVersion
    }

    /// <summary>
    /// Whether anything is expected of the stream; <see langword="false"/> only for <see cref="Any"/>.
    /// </summary>
    public bool HasValue => _case != Case.Any;

    /// <summary>
    /// The expected case, or <see langword="null"/> for <see cref="Any"/>. Reading a value-type version through this
    /// property boxes it; prefer matching on the state itself.
    /// </summary>
    public object? Value => _case switch
    {
        Case.DoesNotExist => default(StreamDoesNotExist),
        Case.Exists => default(StreamExists),
        Case.AtVersion => _version,
        _ => null
    };

    public bool TryGetValue(out StreamDoesNotExist doesNotExist)
    {
        doesNotExist = default;
        return _case == Case.DoesNotExist;
    }

    public bool TryGetValue(out StreamExists exists)
    {
        exists = default;
        return _case == Case.Exists;
    }

    public bool TryGetValue([MaybeNullWhen(false)] out TVersion version)
    {
        version = _version;
        return _case == Case.AtVersion;
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
        // A pattern cannot bind a variable to a case that is a type parameter, so the versions are read directly.
        return this switch
        {
            StreamDoesNotExist => observedState is StreamDoesNotExist,
            StreamExists => observedState is TVersion,
            TVersion =>
                observedState.TryGetValue(out TVersion? observedVersion) &&
                EqualityComparer<TVersion>.Default.Equals(_version, observedVersion),
            null => true
        };
    }

    /// <summary>
    /// Returns a string representation of this expected stream state.
    /// </summary>
    public override string ToString()
    {
        return this switch
        {
            StreamDoesNotExist => "DoesNotExist",
            StreamExists => "Exists",
            TVersion => $"Version={_version}",
            null => "Any"
        };
    }
}