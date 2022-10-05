using System;

namespace DomainBlocks.Core;

public interface IEventNameMap
{
    Type GetEventType(string eventName);
    string GetEventName(Type eventType);
}