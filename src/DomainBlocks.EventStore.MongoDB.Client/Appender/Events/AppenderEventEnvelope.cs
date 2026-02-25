namespace DomainBlocks.EventStore.MongoDB.Client.Appender.Events;

public readonly struct AppenderEventEnvelope(IAppenderEvent @event, AppenderEventSource source)
{
    public IAppenderEvent Event { get; init; } = @event;
    public AppenderEventSource Source { get; init; } = source;
}