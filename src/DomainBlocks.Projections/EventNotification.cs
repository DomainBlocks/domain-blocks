using System;
using DomainBlocks.Projections.New;

namespace DomainBlocks.Projections;

public readonly struct EventNotification<TEventBase>
{
    internal EventNotification(
        EventNotificationKind notificationKind,
        TEventBase @event = default,
        string eventType = null,
        Guid? eventId = null,
        IStreamPosition position = null)
    {
        NotificationKind = notificationKind;
        Event = @event;
        EventType = eventType;
        EventId = eventId ?? Guid.Empty;
        Position = position ?? StreamPosition.Empty;
    }

    public EventNotificationKind NotificationKind { get; }
    public TEventBase Event { get; }
    public string EventType { get; }
    public Guid EventId { get; }
    public IStreamPosition Position { get; }
}

public static class EventNotification
{
    public static EventNotification<TEventBase> CatchingUp<TEventBase>()
    {
        return new EventNotification<TEventBase>(EventNotificationKind.CatchingUp);
    }

    public static EventNotification<TEventBase> CaughtUp<TEventBase>(IStreamPosition position)
    {
        return new EventNotification<TEventBase>(EventNotificationKind.CaughtUp, position: position);
    }

    public static EventNotification<TEventBase> FromEvent<TEventBase>(
        TEventBase @event, string eventType, Guid eventId, IStreamPosition position)
    {
        if (@event == null) throw new ArgumentNullException(nameof(@event));
        return new EventNotification<TEventBase>(EventNotificationKind.Event, @event, eventType, eventId, position);
    }
}