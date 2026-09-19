using System.Runtime.CompilerServices;

namespace DomainBlocks.EventStore;

/// <summary>
/// Provides read origins. Each converts implicitly to a <see cref="ReadOrigin{TPos}"/> of any position type, so the
/// type argument need not be written at the call site. To begin where the read direction naturally does, omit the
/// origin.
/// </summary>
public static class ReadOrigin
{
    /// <summary>
    /// The start of an event sequence.
    /// </summary>
    public static SequenceStart Start => default;

    /// <summary>
    /// The end of an event sequence.
    /// </summary>
    public static SequenceEnd End => default;

    /// <summary>
    /// Creates an origin representing a specific position within an event sequence.
    /// </summary>
    /// <typeparam name="TPos">The type used to represent positions.</typeparam>
    /// <param name="position">The position at which to begin reading.</param>
    /// <returns>An origin representing <paramref name="position"/>.</returns>
    public static ReadOrigin<TPos>.At At<TPos>(TPos position) where TPos : notnull => new(position);
}

/// <summary>
/// Represents a read operation's starting point within an event sequence, i.e., an individual event stream or the
/// global event log.
/// </summary>
/// <remarks>
/// A union of <see cref="SequenceStart"/>, <see cref="SequenceEnd"/> and <see cref="At"/>: each converts implicitly to
/// this type, and a <see langword="switch"/> over all three is exhaustive. The default value names no origin, and
/// matches <see langword="null"/>; a read given it begins where its direction naturally does, which
/// <see cref="ResolveFor"/> makes explicit.
/// </remarks>
/// <typeparam name="TPos"> The type used to represent positions within the event sequence. </typeparam>
[Union]
public readonly record struct ReadOrigin<TPos> : IUnion where TPos : notnull
{
    private readonly TPos? _position;
    private readonly Case _case;

    public ReadOrigin(SequenceStart _)
    {
        _case = Case.Start;
    }

    public ReadOrigin(SequenceEnd _)
    {
        _case = Case.End;
    }

    /// <exception cref="ArgumentNullException">The position of <paramref name="at"/> is <see langword="null"/>.</exception>
    public ReadOrigin(At at)
    {
        ArgumentNullException.ThrowIfNull(at.Position);
        _position = at.Position;
        _case = Case.At;
    }

    private enum Case : byte
    {
        Unspecified,
        Start,
        End,
        At
    }

    /// <summary>
    /// Represents a position within an event sequence. The position is inclusive: a read begins with the event at it.
    /// </summary>
    /// <param name="Position">The position at which to begin reading.</param>
    public readonly record struct At(TPos Position);

    /// <summary>
    /// Whether an origin is named; <see langword="false"/> only for the default value.
    /// </summary>
    public bool HasValue => _case != Case.Unspecified;

    /// <summary>
    /// The origin's case, or <see langword="null"/> for the default value. Reading through this property boxes the
    /// case; prefer matching on the origin itself.
    /// </summary>
    public object? Value => _case switch
    {
        Case.Start => default(SequenceStart),
        Case.End => default(SequenceEnd),
        Case.At => new At(_position!),
        _ => null
    };

    public bool TryGetValue(out SequenceStart start)
    {
        start = default;
        return _case == Case.Start;
    }

    public bool TryGetValue(out SequenceEnd end)
    {
        end = default;
        return _case == Case.End;
    }

    public bool TryGetValue(out At at)
    {
        at = _case == Case.At ? new At(_position!) : default;
        return _case == Case.At;
    }

    /// <summary>
    /// Returns this origin, or where a read in the given direction naturally begins if no origin is named: the start
    /// of the sequence for a forward read, and the end of it for a backward read.
    /// </summary>
    /// <param name="direction">The direction of the read.</param>
    public ReadOrigin<TPos> ResolveFor(ReadDirection direction)
    {
        if (HasValue)
            return this;

        return direction == ReadDirection.Forward ? ReadOrigin.Start : ReadOrigin.End;
    }

    /// <summary>
    /// Returns a string representation of this origin.
    /// </summary>
    public override string ToString()
    {
        return this switch
        {
            SequenceStart => "Start",
            SequenceEnd => "End",
            At => $"At({_position})",
            null => "Unspecified"
        };
    }
}