using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Specifies where reading should begin within a sequence of events. A position may represent the start, end, or a
/// specific position within the sequence. The default value is <see cref="Start"/>.
/// </summary>
public readonly record struct ReadPosition<TPosition> where TPosition : struct
{
    /// <summary>
    /// Logical position at the start of the sequence, before the first event.
    /// </summary>
    public static readonly ReadPosition<TPosition> Start = new(ReadPositionKind.Start);

    /// <summary>
    /// Logical position at the end of the sequence, after the last event.
    /// </summary>
    public static readonly ReadPosition<TPosition> End = new(ReadPositionKind.End);

    private ReadPosition(ReadPositionKind kind, TPosition? specific = null)
    {
        Kind = kind;
        Specific = specific;
    }

    /// <summary>
    /// Gets the kind of this read position.
    /// </summary>
    public ReadPositionKind Kind { get; }

    /// <summary>
    /// Gets the specific position when <see cref="IsSpecific"/> is <c>true</c>, otherwise <c>null</c>.
    /// </summary>
    public TPosition? Specific { get; }

    /// <summary>
    /// True if this read position is <see cref="Start"/>.
    /// </summary>
    public bool IsStart => Kind == ReadPositionKind.Start;

    /// <summary>
    /// True if this read position is <see cref="End"/>.
    /// </summary>
    public bool IsEnd => Kind == ReadPositionKind.End;

    /// <summary>
    /// True if this read position represents a specific position within the sequence.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Specific))]
    public bool IsSpecific => Kind == ReadPositionKind.Specific;

    /// <summary>
    /// Creates a read position representing a specific <paramref name="position"/> within the sequence.
    /// </summary>
    public static ReadPosition<TPosition> At(TPosition position) => new(ReadPositionKind.Specific, position);

    /// <summary>
    /// Creates a read position immediately after <paramref name="position"/> within the sequence.
    /// </summary>
    public static ReadPosition<TPosition> After(TPosition position) => throw new NotImplementedException();

    /// <summary>
    /// Returns a string representation of this read position.
    /// </summary>
    public override string ToString() => Kind switch
    {
        ReadPositionKind.Start => nameof(Start),
        ReadPositionKind.End => nameof(End),
        ReadPositionKind.Specific => $"{Specific}",
        _ => throw new UnreachableException($"Unknown ReadPositionKind '{Kind}'.")
    };
}