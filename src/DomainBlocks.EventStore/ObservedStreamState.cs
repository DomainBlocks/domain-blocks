using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace DomainBlocks.EventStore;

/// <summary>
/// Provides the observed event stream state that carries no version. It converts implicitly to an
/// <see cref="ObservedStreamState{TVersion}"/> of any version type, as a version itself does, so the type argument need
/// not be written at the call site.
/// </summary>
public static class ObservedStreamState
{
    /// <summary>
    /// The stream does not exist.
    /// </summary>
    public static StreamDoesNotExist DoesNotExist => default;
}

/// <summary>
/// Represents the observed state of an event stream at a point in time: either the stream does not exist, or it exists
/// at a version.
/// </summary>
/// <remarks>
/// A union of <see cref="StreamDoesNotExist"/> and <typeparamref name="TVersion"/>: either converts implicitly to this
/// type, and a <see langword="switch"/> over both is exhaustive. The default value means the stream was not observed,
/// and matches <see langword="null"/>.
/// </remarks>
/// <typeparam name="TVersion">The type used to represent the stream version.</typeparam>
[Union]
public readonly record struct ObservedStreamState<TVersion> : IUnion where TVersion : notnull
{
    private readonly TVersion? _version;
    private readonly Case _case;

    public ObservedStreamState(StreamDoesNotExist _)
    {
        _case = Case.DoesNotExist;
    }

    /// <exception cref="ArgumentNullException"><paramref name="version"/> is <see langword="null"/>.</exception>
    public ObservedStreamState(TVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        _version = version;
        _case = Case.AtVersion;
    }

    private enum Case : byte
    {
        NotObserved,
        DoesNotExist,
        AtVersion
    }

    /// <summary>
    /// Whether the stream was observed; <see langword="false"/> only for the default value.
    /// </summary>
    public bool HasValue => _case != Case.NotObserved;

    /// <summary>
    /// The observed case, or <see langword="null"/> if the stream was not observed. Reading a value-type version
    /// through this property boxes it; prefer matching on the state itself.
    /// </summary>
    public object? Value => _case switch
    {
        Case.DoesNotExist => default(StreamDoesNotExist),
        Case.AtVersion => _version,
        _ => null
    };

    public bool TryGetValue(out StreamDoesNotExist doesNotExist)
    {
        doesNotExist = default;
        return _case == Case.DoesNotExist;
    }

    public bool TryGetValue([MaybeNullWhen(false)] out TVersion version)
    {
        version = _version;
        return _case == Case.AtVersion;
    }

    /// <summary>
    /// Returns a string representation of this observed stream state.
    /// </summary>
    public override string ToString()
    {
        return _case switch
        {
            Case.DoesNotExist => "DoesNotExist",
            Case.AtVersion => $"Version={_version}",
            _ => "NotObserved"
        };
    }
}