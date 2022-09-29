using System;

namespace DomainBlocks.Core;

public interface IEventType
{
    public Type ClrType { get; }
    public string EventName { get; }
}