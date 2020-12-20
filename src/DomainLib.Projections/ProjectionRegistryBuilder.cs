using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainLib.Projections
{
    public sealed class ProjectionRegistryBuilder
    {
        private readonly IList<IEventProjectionBuilder> _eventProjectionBuilders = new List<IEventProjectionBuilder>();
        private readonly EventProjectionMap _eventProjectionMap = new();
        private readonly ProjectionEventNameMap _eventNameMap = new();
        private readonly EventContextMap _eventContextMap = new();

        public EventProjectionBuilder<TEvent> Event<TEvent>()
        {
            var builder = new EventProjectionBuilder<TEvent>(this);
            _eventProjectionBuilders.Add(builder);

            return builder;
        }

        public ProjectionRegistry Build()
        {
            foreach (var (eventType, projectionType, func) in _eventProjectionBuilders.SelectMany(epb => epb.BuildProjectionFuncs()))
            {
                _eventProjectionMap.AddProjectionFunc(eventType, projectionType, func);
            }

            return new ProjectionRegistry(_eventProjectionMap, _eventContextMap, _eventNameMap);
        }

        internal void RegisterEventName<TEvent>(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            _eventNameMap.RegisterTypeForEventName<TEvent>(name);
        }

        internal void RegisterContextForEvent<TEvent>(IContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            _eventContextMap.RegisterContextForEvent<TEvent>(context);
        }
    }
}