using System;
using System.Collections.Generic;
using DomainBlocks.Serialization;

namespace DomainBlocks.Projections
{
    public class EventNotification<TEventBase>
    {
        internal EventNotification(EventNotificationKind notificationKind, IEnumerable<IReadEvent<TEventBase>> events)
        {
            NotificationKind = notificationKind;
            Events = events;
        }

        public EventNotificationKind NotificationKind { get; }

        public IEnumerable<IReadEvent<TEventBase>> Events { get; }
    }

    public static class EventNotification
    {
        public static EventNotification<TEventBase> CaughtUp<TEventBase>()
        {
            return new(EventNotificationKind.CaughtUpNotification, default);
        }

        public static EventNotification<TEventBase> FromEvents<TEventBase>(IEnumerable<IReadEvent<TEventBase>> events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            return new EventNotification<TEventBase>(EventNotificationKind.Event, events);
        }
    }
}