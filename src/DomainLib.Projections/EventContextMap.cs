using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace DomainLib.Projections
{
    public sealed class EventContextMap
    {
        private readonly HashSet<IContext> _allContexts = new();
        private readonly Dictionary<Type, HashSet<IContext>> _eventContextMap = new();
        // ReSharper disable once CollectionNeverUpdated.Local
        private static readonly Collection<IContext> EmptyContextCollection = new();

        public void RegisterContextForEvent<TEvent>(IContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            _allContexts.Add(context);

            if (_eventContextMap.TryGetValue(typeof(TEvent), out var contexts))
            {
                contexts.Add(context);
            }
            else
            {
                var contextsList = new HashSet<IContext>() {context};
                _eventContextMap.Add(typeof(TEvent), contextsList);
            }
        }

        public IEnumerable<IContext> GetAllContexts()
        {
            return _allContexts;
        }

        public IReadOnlyCollection<IContext> GetContextsForEventType(Type eventType)
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