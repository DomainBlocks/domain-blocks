using System;

namespace DomainBlocks.Core;

public interface IEventOptions
{
    Type ClrType { get; }
    string EventName { get; }
}

public interface IEventOptions<TAggregate> : IEventOptions
{
    TAggregate ApplyEvent(TAggregate aggregate, object @event);
}