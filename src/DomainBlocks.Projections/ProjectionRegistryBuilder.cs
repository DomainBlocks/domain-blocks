using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Projections
{
    public sealed class ProjectionRegistryBuilder
    {
        private readonly IList<IEventProjectionBuilder> _eventProjectionBuilders = new List<IEventProjectionBuilder>();
        private readonly EventProjectionMap _eventProjectionMap = new();
        private readonly ProjectionEventNameMap _eventNameMap = new();
        private readonly ProjectionContextMap _projectionContextMap = new();

        public EventProjectionBuilder<TEvent> Event<TEvent>()
        {
            var builder = new EventProjectionBuilder<TEvent>(this);
            _eventProjectionBuilders.Add(builder);

            // Default event name to the .NET type.
            // This can be overridden by explicitly
            // specifying a name/names in the fluent builder
            RegisterDefaultEventName<TEvent>();

            return builder;
        }

        public ProjectionRegistry Build()
        {
            foreach (var (eventType, projectionType, func) in _eventProjectionBuilders.SelectMany(epb => epb.BuildProjectionFuncs()))
            {
                _eventProjectionMap.AddProjectionFunc(eventType, projectionType, func);
            }

            return new ProjectionRegistry(_eventProjectionMap, _projectionContextMap, _eventNameMap);
        }

        private void RegisterDefaultEventName<TEvent>()
        {
            _eventNameMap.RegisterDefaultEventName<TEvent>();
        }

        internal void OverrideEventNames<TEvent>(params string[] names)
        {
            if (names == null) throw new ArgumentNullException(nameof(names));
            if (names.Length == 0) throw new ArgumentException("Value cannot be an empty collection.", nameof(names));

            _eventNameMap.OverrideEventNames<TEvent>(names);
        }

        internal void RegisterContextForEvent<TEvent>(IProjectionContext projectionContext)
        {
            if (projectionContext == null) throw new ArgumentNullException(nameof(projectionContext));
            _projectionContextMap.RegisterProjectionContext<TEvent>(projectionContext);
        }
    }
}