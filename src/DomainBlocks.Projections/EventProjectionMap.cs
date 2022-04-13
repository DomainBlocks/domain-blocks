using System;
using System.Collections.Generic;

namespace DomainBlocks.Projections
{
    public sealed class EventProjectionMap : Dictionary<Type, IList<RunProjection>>
    {
        public void AddProjectionFunc(Type eventType, RunProjection projectionFunc)
        {
            if (eventType == null) throw new ArgumentNullException(nameof(eventType));
            if (projectionFunc == null) throw new ArgumentNullException(nameof(projectionFunc));

            if (TryGetValue(eventType, out var projectionFuncs))
            {
                projectionFuncs.Add(projectionFunc);
            }
            else
            {
                var projectionsList = new List<RunProjection> { projectionFunc };
                Add(eventType, projectionsList);
            }
        }
    }
}