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
}

/// <summary>
/// A message delivered by a subscription: either an event, or a notification that the subscription has caught up or
/// fallen behind. Property patterns are the intended way to consume it:
/// <code>
/// switch (message)
/// {
///     case { Event: { } e }: ...
///     case { IsCaughtUp: true }: ...
///     case { IsFellBehind: true }: ...
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

    private readonly ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos> _event;

    internal SubscriptionMessage(
        SubscriptionMessageKind kind,
        ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos> @event)
    {
        Kind = kind;
        _event = @event;
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

    /// <inheritdoc/>
    public override string ToString() => Kind == SubscriptionMessageKind.Event
        ? $"Event({_event.Context.StreamId}@{_event.Context.StreamPosition})"
        : Kind.ToString();
}