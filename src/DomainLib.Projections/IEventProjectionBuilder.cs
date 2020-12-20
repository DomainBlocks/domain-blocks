using System;
using System.Collections.Generic;

namespace DomainLib.Projections
{
    public interface IEventProjectionBuilder
    {
        IEnumerable<(Type eventType, Type projectionType, RunProjection func)> BuildProjectionFuncs();
    }
}