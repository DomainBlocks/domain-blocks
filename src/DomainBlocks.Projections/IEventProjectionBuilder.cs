using System;
using System.Collections.Generic;

namespace DomainBlocks.Projections
{
    public interface IEventProjectionBuilder
    {
        IEnumerable<(Type eventType, RunProjection func)> BuildProjectionFuncs();
    }
}