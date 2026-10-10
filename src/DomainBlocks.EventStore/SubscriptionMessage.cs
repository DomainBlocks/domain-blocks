namespace DomainBlocks.EventStore;

/// <summary>
/// Provides methods for creating <see cref="SubscriptionMessage{TEvent,TStreamId,TStreamPos,TLogPos}"/> values.
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
}

/// <summary>
/// Represents a message from a subscription: an event, or a notice that the subscription caught up or fell behind.
/// </summary>
/// <remarks>
/// Consume it with property patterns:
/// <code>
/// switch (message)
/// {
///     case { Event: { } e }: ...
///     case { IsCaughtUp: true }: ...
///     case { IsFellBehind: true }: ...
/// }
/// </code>
/// </remarks>
/// <typeparam name="TEvent">The type of the events.</typeparam>
/// <typeparam name="TStreamId">The type of a stream identifier.</typeparam>
/// <typeparam name="TStreamPos">The type of a position within a stream.</typeparam>
/// <typeparam name="TLogPos">The type of a position in the event log.</typeparam>
public readonly struct SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    /// <summary>
    /// A <see cref="SubscriptionMessageKind.CaughtUp"/> message.
    /// </summary>
    public static readonly SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos> CaughtUp =
        new(SubscriptionMessageKind.CaughtUp, default);

    /// <summary>
    /// A <see cref="SubscriptionMessageKind.FellBehind"/> message.
    /// </summary>
    public static readonly SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos> FellBehind =
        new(SubscriptionMessageKind.FellBehind, default);

    private readonly ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos> _event;

    internal SubscriptionMessage(
        SubscriptionMessageKind kind,
        ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos> @event)
    {
        Kind = kind;
        _event = @event;
    }

    /// <summary>
    /// Gets the kind of message.
    /// </summary>
    public SubscriptionMessageKind Kind { get; }

    /// <summary>
    /// Gets the event, or <see langword="null"/> if the message does not carry one.
    /// </summary>
    public ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>? Event =>
        Kind == SubscriptionMessageKind.Event ? _event : null;

    /// <summary>
    /// Gets a value that indicates whether this is a <see cref="SubscriptionMessageKind.CaughtUp"/> message.
    /// </summary>
    public bool IsCaughtUp => Kind == SubscriptionMessageKind.CaughtUp;

    /// <summary>
    /// Gets a value that indicates whether this is a <see cref="SubscriptionMessageKind.FellBehind"/> message.
    /// </summary>
    public bool IsFellBehind => Kind == SubscriptionMessageKind.FellBehind;

    /// <inheritdoc/>
    public override string ToString() => Kind == SubscriptionMessageKind.Event
        ? $"Event({_event.Context.StreamId}@{_event.Context.StreamPosition})"
        : Kind.ToString();
}