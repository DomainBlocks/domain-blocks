using DomainBlocks.Core;

namespace DomainBlocks.EventStore;

/// <summary>
/// Provides factory methods for creating subscription messages.
/// </summary>
public static class SubscriptionMessage
{
    /// <summary>
    /// Creates a message that carries an event.
    /// </summary>
    public static SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>
        Event<TEvent, TStreamId, TStreamPos, TLogPos>(ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos> @event)
        where TEvent : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        return new SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>(SubscriptionMessageKind.Event, @event);
    }

    /// <summary>
    /// A checkpoint of a subscription to the log. See <see cref="SubscriptionMessageKind.Checkpoint"/>.
    /// </summary>
    public static SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>
        LogCheckpoint<TEvent, TStreamId, TStreamPos, TLogPos>(TLogPos position)
        where TEvent : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        ArgumentNullException.ThrowIfNull(position);

        return new SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>(
            default!,
            position,
            isStreamCheckpoint: false);
    }

    /// <summary>
    /// A checkpoint of a subscription to a stream. See <see cref="SubscriptionMessageKind.Checkpoint"/>.
    /// </summary>
    public static SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>
        StreamCheckpoint<TEvent, TStreamId, TStreamPos, TLogPos>(TStreamPos position)
        where TEvent : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        ArgumentNullException.ThrowIfNull(position);

        return new SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>(
            position,
            default!,
            isStreamCheckpoint: true);
    }
}

/// <summary>
/// A message delivered by a subscription: either an event, or a notification that the subscription has caught up or
/// fallen behind, or, from a subscription with a filter, a checkpoint that says how far it has looked. Property
/// patterns are the intended way to consume it:
/// <code>
/// switch (message)
/// {
///     case { Event: { } e }: ...
///     case { IsCaughtUp: true }: ...
///     case { IsFellBehind: true }: ...
///     case { LogCheckpoint: { HasValue: true, Value: var position } }: ...
/// }
/// </code>
/// </summary>
/// <typeparam name="TEvent">The type of events stored by the event store.</typeparam>
/// <typeparam name="TStreamId">The type used to identify event streams.</typeparam>
/// <typeparam name="TStreamPos">The type used to represent positions within a stream.</typeparam>
/// <typeparam name="TLogPos">The type used to represent positions in the global event log.</typeparam>
public readonly struct SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    /// <summary>
    /// The message that reports the subscription has caught up. See <see cref="SubscriptionMessageKind.CaughtUp"/>.
    /// </summary>
    public static readonly SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos> CaughtUp =
        new(SubscriptionMessageKind.CaughtUp, default);

    /// <summary>
    /// The message that reports the subscription has fallen behind. See
    /// <see cref="SubscriptionMessageKind.FellBehind"/>.
    /// </summary>
    public static readonly SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos> FellBehind =
        new(SubscriptionMessageKind.FellBehind, default);

    // Of an event, or of a checkpoint, which keeps its position in the context of an event that is otherwise empty, so
    // that a message is no bigger for being able to carry one.
    private readonly ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos> _event;

    private readonly bool _isStreamCheckpoint;

    internal SubscriptionMessage(
        SubscriptionMessageKind kind,
        ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos> @event)
    {
        Kind = kind;
        _event = @event;
    }

    // Only the position that the flag says is given.
    internal SubscriptionMessage(TStreamPos streamPosition, TLogPos logPosition, bool isStreamCheckpoint)
    {
        Kind = SubscriptionMessageKind.Checkpoint;
        _isStreamCheckpoint = isStreamCheckpoint;

        _event = new ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>(
            default!,
            new ReadEventContext<TStreamId, TStreamPos, TLogPos>(
                default!,
                null!,
                null!,
                default,
                streamPosition,
                logPosition));
    }

    /// <summary>
    /// What this message carries.
    /// </summary>
    public SubscriptionMessageKind Kind { get; }

    /// <summary>
    /// The event, when <see cref="Kind"/> is <see cref="SubscriptionMessageKind.Event"/>; otherwise
    /// <see langword="null"/>.
    /// </summary>
    public ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>? Event =>
        Kind == SubscriptionMessageKind.Event ? _event : null;

    /// <summary>
    /// Whether <see cref="Kind"/> is <see cref="SubscriptionMessageKind.CaughtUp"/>.
    /// </summary>
    public bool IsCaughtUp => Kind == SubscriptionMessageKind.CaughtUp;

    /// <summary>
    /// Whether <see cref="Kind"/> is <see cref="SubscriptionMessageKind.FellBehind"/>.
    /// </summary>
    public bool IsFellBehind => Kind == SubscriptionMessageKind.FellBehind;

    /// <summary>
    /// Whether <see cref="Kind"/> is <see cref="SubscriptionMessageKind.Checkpoint"/>. The position is in
    /// <see cref="LogCheckpoint"/> for a subscription to the log, and in <see cref="StreamCheckpoint"/> for one to a
    /// stream.
    /// </summary>
    public bool IsCheckpoint => Kind == SubscriptionMessageKind.Checkpoint;

    /// <summary>
    /// The position of a checkpoint of a subscription to the log, to resume after. Every event that the subscription
    /// selects up to it has been delivered.
    /// </summary>
    public Optional<TLogPos> LogCheckpoint =>
        IsCheckpoint && !_isStreamCheckpoint ? Optional.From(_event.Context.LogPosition) : default;

    /// <summary>
    /// The position of a checkpoint of a subscription to a stream, to resume after. Every event of the stream that the
    /// subscription selects up to it has been delivered.
    /// </summary>
    public Optional<TStreamPos> StreamCheckpoint =>
        IsCheckpoint && _isStreamCheckpoint ? Optional.From(_event.Context.StreamPosition) : default;

    /// <inheritdoc/>
    public override string ToString()
    {
        return Kind switch
        {
            SubscriptionMessageKind.Event => $"Event({_event.Context.StreamId}@{_event.Context.StreamPosition})",
            SubscriptionMessageKind.Checkpoint when _isStreamCheckpoint => $"Checkpoint(stream@{StreamCheckpoint})",
            SubscriptionMessageKind.Checkpoint => $"Checkpoint(log@{LogCheckpoint})",
            _ => Kind.ToString()
        };
    }
}