namespace DomainBlocks.Core;

public interface IEventType
{
    Type ClrType { get; }
    string EventName { get; }
}