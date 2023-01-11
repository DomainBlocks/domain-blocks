using System.Runtime.Serialization;

namespace DomainBlocks.Core.Projections;

[Serializable]
public class EventStreamException : Exception
{
    public EventStreamException()
    {
    }

    public EventStreamException(string message) : base(message)
    {
    }

    public EventStreamException(string message, Exception inner) : base(message, inner)
    {
    }

    protected EventStreamException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}