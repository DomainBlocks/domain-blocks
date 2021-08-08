using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace DomainLib.Projections
{
    public sealed class ProjectionContextMap
    {
        private readonly HashSet<IProjectionContext> _allContexts = new();
        private readonly Dictionary<Type, HashSet<IProjectionContext>> _eventContextMap = new();
        // ReSharper disable once CollectionNeverUpdated.Local
        private static readonly Collection<IProjectionContext> EmptyContextCollection = new();

        public void RegisterProjectionContext<TEvent>(IProjectionContext projectionContext)
        {
            if (projectionContext == null) throw new ArgumentNullException(nameof(projectionContext));

            _allContexts.Add(projectionContext);

            if (_eventContextMap.TryGetValue(typeof(TEvent), out var contexts))
            {
                contexts.Add(projectionContext);
            }
            else
            {
                var contextsList = new HashSet<IProjectionContext>() {projectionContext};
                _eventContextMap.Add(typeof(TEvent), contextsList);
            }
        }

        public IEnumerable<IProjectionContext> GetAllContexts()
        {
            return _allContexts;
        }

        public IReadOnlyCollection<IProjectionContext> GetContextsForEventType(Type eventType)
        {
            if (eventType == null) throw new ArgumentNullException(nameof(eventType));
            if (_eventContextMap.TryGetValue(eventType, out var contexts))
            {
                return contexts;
            }

            return EmptyContextCollection;
        }
    }
}