using System;
using System.Collections.Generic;

namespace DomainBlocks.Projections
{
    public interface IProjectionBuilder
    {
        IEnumerable<(Type eventType, Type projectionType, RunProjection func)> BuildProjections();
    }
}