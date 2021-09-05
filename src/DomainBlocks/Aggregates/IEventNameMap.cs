using System;

namespace DomainBlocks.Aggregates
{
    public interface IEventNameMap
    {
        Type GetClrTypeForEventName(string eventName);
        string GetEventNameForClrType(Type clrType);
    }
}