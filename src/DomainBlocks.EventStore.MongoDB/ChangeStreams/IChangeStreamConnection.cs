namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

internal interface IChangeStreamConnection : IAsyncDisposable
{
    Task Completion { get; }
}