using System;
using System.Collections.Generic;

namespace DomainBlocks.Projections
{
    public interface IProjectionBuilder
    {
        IEnumerable<(Type eventType, RunProjection func)> BuildProjections();
    }
}