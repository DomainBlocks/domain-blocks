using System;

namespace DomainBlocks.Aggregates
{
    public interface IEventNameMap
    {
        Type GetEventType(string eventName);
        string GetEventName(Type eventType);
    }
}