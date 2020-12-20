using System;
using System.Collections.Generic;

namespace DomainLib.Projections
{
    public interface IProjectionBuilder
    {
        IEnumerable<(Type eventType, Type projectionType, RunProjection func)> BuildProjections();
    }
}