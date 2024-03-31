namespace DomainBlocks.Abstractions;

public interface IStreamSubscription : IDisposable
{
    IAsyncEnumerable<IStreamMessage> ReadMessagesAsync(CancellationToken cancellationToken = default);
}