using System;

namespace DomainBlocks.Projections;

public readonly struct EventNotification<TEventBase>
{
    internal EventNotification(
        EventNotificationKind notificationKind, TEventBase @event, string eventType, Guid eventId)
    {
        NotificationKind = notificationKind;
        Event = @event;
        EventType = eventType;
        EventId = eventId;
    }

    public EventNotificationKind NotificationKind { get; }
    public TEventBase Event { get; }
    public string EventType { get; }
    public Guid EventId { get; }
}

public static class EventNotification
{
    public static EventNotification<TEventBase> CatchingUp<TEventBase>()
    {
        return new EventNotification<TEventBase>(EventNotificationKind.CatchingUp, default, null, Guid.Empty);
    }
    
    public static EventNotification<TEventBase> CaughtUp<TEventBase>()
    {
        return new EventNotification<TEventBase>(EventNotificationKind.CaughtUp, default, null, Guid.Empty);
    }

    public static EventNotification<TEventBase> FromEvent<TEventBase>(
        TEventBase @event, string eventType, Guid eventId)
    {
        if (@event == null) throw new ArgumentNullException(nameof(@event));
        return new EventNotification<TEventBase>(EventNotificationKind.Event, @event, eventType, eventId);
    }
}