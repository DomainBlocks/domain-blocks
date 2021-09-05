using System;
using System.Collections.Generic;

namespace DomainBlocks.Projections
{
    public interface IProjectionEventNameMap
    {
        IEnumerable<Type> GetClrTypesForEventName(string eventName);
    }
}