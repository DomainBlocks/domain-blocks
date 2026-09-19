using System.Runtime.CompilerServices;

namespace DomainBlocks.EventStore;

/// <summary>
/// Provides subscription origins. Each converts implicitly to a <see cref="SubscriptionOrigin{TPos}"/> of any position
/// type, so the type argument need not be written at the call site. Omitting the origin subscribes from the end.
/// </summary>
public static class SubscriptionOrigin
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
    /// Creates an origin representing the point immediately after a specific position within an event sequence.
    /// </summary>
    /// <typeparam name="TPos">The type used to represent positions.</typeparam>
    /// <param name="position">The position after which to begin receiving events.</param>
    /// <returns>An origin representing the point immediately after <paramref name="position"/>.</returns>
    public static SubscriptionOrigin<TPos>.After After<TPos>(TPos position) where TPos : notnull => new(position);
}

/// <summary>
/// Represents a subscription’s starting point within an event sequence, i.e., an individual event stream or the
/// global event log.
/// </summary>
/// <remarks>
/// A union of <see cref="SequenceStart"/>, <see cref="SequenceEnd"/> and <see cref="After"/>: each converts implicitly
/// to this type, and a <see langword="switch"/> over all three is exhaustive. The default value names no origin, and
/// matches <see langword="null"/>; a subscription given it begins at the end of the sequence, which
/// <see cref="Resolve"/> makes explicit.
/// </remarks>
/// <typeparam name="TPos">The type used to represent positions within the event sequence.</typeparam>
[Union]
public readonly record struct SubscriptionOrigin<TPos> : IUnion where TPos : notnull
{
    private readonly TPos? _position;
    private readonly Case _case;

    public SubscriptionOrigin(SequenceStart _)
    {
        _case = Case.Start;
    }

    public SubscriptionOrigin(SequenceEnd _)
    {
        _case = Case.End;
    }

    /// <exception cref="ArgumentNullException">
    /// The position of <paramref name="after"/> is <see langword="null"/>.
    /// </exception>
    public SubscriptionOrigin(After after)
    {
        ArgumentNullException.ThrowIfNull(after.Position);
        _position = after.Position;
        _case = Case.After;
    }

    private enum Case : byte
    {
        Unspecified,
        Start,
        End,
        After
    }

    /// <summary>
    /// Represents a position after which to begin receiving events. The position is exclusive: the subscription begins
    /// with the event that follows it.
    /// </summary>
    /// <param name="Position">The position after which to begin receiving events.</param>
    public readonly record struct After(TPos Position);

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
        Case.After => new After(_position!),
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

    public bool TryGetValue(out After after)
    {
        after = _case == Case.After ? new After(_position!) : default;
        return _case == Case.After;
    }

    /// <summary>
    /// Returns this origin, or the end of the sequence if no origin is named.
    /// </summary>
    public SubscriptionOrigin<TPos> Resolve()
    {
        return HasValue ? this : SubscriptionOrigin.End;
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
            After => $"After({_position})",
            null => "Unspecified"
        };
    }
}