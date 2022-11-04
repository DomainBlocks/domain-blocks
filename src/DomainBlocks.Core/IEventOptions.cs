using System;

namespace DomainBlocks.Core;

public interface IEventOptions
{
    Type ClrType { get; }
    string EventName { get; }
}