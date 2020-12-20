using System;

namespace DomainLib.Projections
{
    public class ProjectionRegistry
    {
        public ProjectionRegistry(EventProjectionMap eventProjectionMap,
                                  EventContextMap eventContextMap,
                                  IProjectionEventNameMap eventNameMap)
        {
            EventProjectionMap = eventProjectionMap ?? throw new ArgumentNullException(nameof(eventProjectionMap));
            EventContextMap = eventContextMap ?? throw new ArgumentNullException(nameof(eventContextMap));
            EventNameMap = eventNameMap ?? throw new ArgumentNullException(nameof(eventNameMap));
        }

        public EventProjectionMap EventProjectionMap { get; }
        public IProjectionEventNameMap EventNameMap { get; }
        public EventContextMap EventContextMap { get; }
    }
}