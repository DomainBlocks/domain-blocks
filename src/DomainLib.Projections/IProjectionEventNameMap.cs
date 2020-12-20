using System;
using System.Collections.Generic;

namespace DomainLib.Projections
{
    public interface IProjectionEventNameMap
    {
        IEnumerable<Type> GetClrTypesForEventName(string eventName);
    }
}