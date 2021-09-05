using System;
using System.Collections.Generic;

namespace DomainBlocks.Projections
{
    public interface IEventProjectionBuilder
    {
        IEnumerable<(Type eventType, Type projectionType, RunProjection func)> BuildProjectionFuncs();
    }
}