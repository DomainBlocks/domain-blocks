using System;

namespace DomainBlocks.Persistence.New;

public interface IEventType
{
    public Type ClrType { get; }
    public string EventName { get; }
}