namespace DomainBlocks.Infrastructure.MongoDB.ChangeStreams;

public interface IChangeStreamConnection : IAsyncDisposable
{
    Task Completion { get; }
}