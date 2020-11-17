using System;

namespace DomainLib.Aggregates
{
    public interface IEventNameMap
    {
        Type GetClrTypeForEventName(string eventName);
        string GetEventNameForClrType(Type clrType);
    }
}