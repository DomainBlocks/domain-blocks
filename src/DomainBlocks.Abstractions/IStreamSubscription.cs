namespace DomainBlocks.Abstractions;

public interface IStreamSubscription : IDisposable
{
    IAsyncEnumerable<IStreamMessage> ConsumeAsync(CancellationToken cancellationToken = default);
}