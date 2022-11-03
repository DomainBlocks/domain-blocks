using System;

namespace DomainBlocks.Core;

public interface IEventOptions
{
    public Type ClrType { get; }
    public string EventName { get; }
}