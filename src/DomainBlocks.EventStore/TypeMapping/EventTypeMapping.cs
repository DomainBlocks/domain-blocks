using System.Diagnostics;
using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore.TypeMapping;

public abstract record EventTypeMapping
{
    private EventTypeMapping()
    {
    }

    internal void ValidateAssignableTo(Type targetType)
    {
        var eventType = this switch
        {
            Write w => w.EventType,
            ReadToType r => r.EventType,
            ReadToInstance r => r.Instance.GetType(),
            _ => throw new UnreachableException($"Unhandled mapping type '{GetType()}'.")
        };

        if (!eventType.IsAssignableTo(targetType))
        {
            throw new DomainBlocksException(
                $"Invalid mapping [{this}]: typeof({eventType.Name}) is not assignable to typeof({targetType.Name}).");
        }
    }

    public sealed record Write(Type EventType, string EventName) : EventTypeMapping
    {
        public override string ToString() => $"Write typeof({EventType.Name}) -> '{EventName}'";
    }

    public abstract record Read : EventTypeMapping
    {
        private protected Read(string eventName)
        {
            EventName = eventName;
        }

        public string EventName { get; }
    }

    public sealed record ReadToType(string EventName, Type EventType) : Read(EventName)
    {
        public override string ToString() => $"Read '{EventName}' -> typeof({EventType.Name})";
    }

    public sealed record ReadToInstance(string EventName, object Instance) : Read(EventName)
    {
        public override string ToString() => $"Read '{EventName}' -> instance of {Instance.GetType().Name}";
    }
}