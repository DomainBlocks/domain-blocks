using System.Runtime.CompilerServices;

namespace DomainBlocks.EventStore;

/// <summary>
/// Provides the subscription messages that carry no event. Each converts implicitly to a
/// <see cref="SubscriptionMessage{TEvent,TStreamId,TStreamPos,TLogPos}"/> of any type arguments, as a
/// <see cref="ReadEvent{TEvent,TStreamId,TStreamPos,TLogPos}"/> does, so they need not be written at the call site.
/// </summary>
public static class SubscriptionMessage
{
    /// <summary>
    /// The message that reports the subscription has caught up.
    /// </summary>
    public static SubscriptionCaughtUp CaughtUp => default;

    /// <summary>
    /// The message that reports the subscription has fallen behind.
    /// </summary>
    public static SubscriptionFellBehind FellBehind => default;
}

/// <summary>
/// A message delivered by a subscription: either an event, or a notification that the subscription has caught up or
/// fallen behind.
/// </summary>
/// <remarks>
/// A union of <see cref="ReadEvent{TEvent,TStreamId,TStreamPos,TLogPos}"/>, <see cref="SubscriptionCaughtUp"/> and
/// <see cref="SubscriptionFellBehind"/>, so a <see langword="switch"/> over all three is exhaustive:
/// <code>
/// var text = message switch
/// {
///     ReadEvent&lt;MyEvent, string, StreamPosition, LogPosition&gt; e => ...,
///     SubscriptionCaughtUp => ...,
///     SubscriptionFellBehind => ...
/// };
/// </code>
/// Where that check is not needed, <see cref="Event"/> reaches the event without naming its type:
/// <code>
/// switch (message)
/// {
///     case { Event: { } e }: ...
///     case SubscriptionCaughtUp: ...
///     case SubscriptionFellBehind: ...
/// }
/// </code>
/// A subscription never delivers the default value, which holds no message and matches <see langword="null"/>.
/// </remarks>
/// <typeparam name="TEvent">The type of events stored by the event store.</typeparam>
/// <typeparam name="TStreamId">The type used to identify event streams.</typeparam>
/// <typeparam name="TStreamPos">The type used to represent positions within a stream.</typeparam>
/// <typeparam name="TLogPos">The type used to represent positions in the global event log.</typeparam>
[Union]
public readonly struct SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos> : IUnion
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    private readonly ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos> _event;
    private readonly Case _case;

    public SubscriptionMessage(ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos> @event)
    {
        _event = @event;
        _case = Case.Event;
    }

    public SubscriptionMessage(SubscriptionCaughtUp _)
    {
        _case = Case.CaughtUp;
    }

    public SubscriptionMessage(SubscriptionFellBehind _)
    {
        _case = Case.FellBehind;
    }

    private enum Case : byte
    {
        None,
        Event,
        CaughtUp,
        FellBehind
    }

    /// <summary>
    /// The event, if this message carries one; otherwise <see langword="null"/>. A shorthand for matching the
    /// <see cref="ReadEvent{TEvent,TStreamId,TStreamPos,TLogPos}"/> case, whose type is long to write:
    /// <c>if (message.Event is { } e)</c>, or <c>case { Event: { } e }:</c> beside the other cases in a
    /// <see langword="switch"/> statement. A pattern on this property is not a pattern on the case, so it does not
    /// count towards a <see langword="switch"/> expression being exhaustive.
    /// </summary>
    public ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>? Event => _case == Case.Event ? _event : null;

    /// <summary>
    /// Whether this holds a message; <see langword="false"/> only for the default value.
    /// </summary>
    public bool HasValue => _case != Case.None;

    /// <summary>
    /// The message's case, or <see langword="null"/> for the default value. Reading an event through this property
    /// boxes it; prefer matching on the message itself.
    /// </summary>
    public object? Value => _case switch
    {
        Case.Event => _event,
        Case.CaughtUp => default(SubscriptionCaughtUp),
        Case.FellBehind => default(SubscriptionFellBehind),
        _ => null
    };

    public bool TryGetValue(out ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos> @event)
    {
        @event = _event;
        return _case == Case.Event;
    }

    public bool TryGetValue(out SubscriptionCaughtUp caughtUp)
    {
        caughtUp = default;
        return _case == Case.CaughtUp;
    }

    public bool TryGetValue(out SubscriptionFellBehind fellBehind)
    {
        fellBehind = default;
        return _case == Case.FellBehind;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return _case switch
        {
            Case.Event => $"Event({_event.Context.StreamId}@{_event.Context.StreamPosition})",
            Case.CaughtUp => "CaughtUp",
            Case.FellBehind => "FellBehind",
            _ => "None"
        };
    }
}