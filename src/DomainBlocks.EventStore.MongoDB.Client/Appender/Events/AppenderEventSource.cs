namespace DomainBlocks.EventStore.MongoDB.Client.Appender.Events;

public enum AppenderEventSource
{
    Replay,
    ChangeStream,
    LocalNode
}