using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainLib.Projections
{
    internal sealed class ProjectionEventNameMap : IProjectionEventNameMap
    {
        private readonly Dictionary<string, HashSet<Type>> _eventNameMap = new();

        public IEnumerable<Type> GetClrTypesForEventName(string eventName)
        {
            if (eventName == null) throw new ArgumentNullException(nameof(eventName));
            return _eventNameMap.TryGetValue(eventName, out var types) ? 
                       types : 
                       Enumerable.Empty<Type>();
        }

        public void RegisterTypeForEventName<TEvent>(string eventName)
        {
            if (eventName == null) throw new ArgumentNullException(nameof(eventName));
            if (_eventNameMap.TryGetValue(eventName, out var types))
            {
                types.Add(typeof(TEvent));
            }
            else
            {
                var typesSet = new HashSet<Type> {typeof(TEvent)};
                _eventNameMap.Add(eventName, typesSet);
            }
        }
    }
}