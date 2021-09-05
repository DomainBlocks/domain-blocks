using System;

namespace DomainBlocks.Projections
{
    public class ProjectionRegistry
    {
        public ProjectionRegistry(EventProjectionMap eventProjectionMap,
                                  ProjectionContextMap projectionContextMap,
                                  IProjectionEventNameMap eventNameMap)
        {
            EventProjectionMap = eventProjectionMap ?? throw new ArgumentNullException(nameof(eventProjectionMap));
            ProjectionContextMap = projectionContextMap ?? throw new ArgumentNullException(nameof(projectionContextMap));
            EventNameMap = eventNameMap ?? throw new ArgumentNullException(nameof(eventNameMap));
        }

        public EventProjectionMap EventProjectionMap { get; }
        public IProjectionEventNameMap EventNameMap { get; }
        public ProjectionContextMap ProjectionContextMap { get; }
    }
}