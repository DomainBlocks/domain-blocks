using DomainBlocks.EventStore.MongoDB.Client.Appender.Events;

namespace DomainBlocks.EventStore.MongoDB.Client.Appender;

public sealed class AppenderProcess
{
    public HandleEventResult HandleEvent(AppenderEventEnvelope envelope)
    {
        return new HandleEventResult();
    }
}