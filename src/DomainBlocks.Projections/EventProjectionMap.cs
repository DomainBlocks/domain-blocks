using System;
using System.Collections.Generic;

namespace DomainBlocks.Projections
{
    public sealed class EventProjectionMap : Dictionary<Type, IList<(Type, RunProjection)>>
    {
        public void AddProjectionFunc(Type eventType, Type projectionType, RunProjection projectionFunc)
        {
            if (eventType == null) throw new ArgumentNullException(nameof(eventType));
            if (projectionType == null) throw new ArgumentNullException(nameof(projectionType));
            if (projectionFunc == null) throw new ArgumentNullException(nameof(projectionFunc));

            if (TryGetValue(eventType, out var projectionFuncs))
            {
                projectionFuncs.Add((projectionType, projectionFunc));
            }
            else
            {
                var projectionsList = new List<(Type, RunProjection)> {(projectionType, projectionFunc)};
                Add(eventType, projectionsList);
            }
        }
    }
}