namespace DomainBlocks.V1.Abstractions;

public interface IStreamSubscription : IDisposable
{
    IAsyncEnumerable<IStreamMessage> ConsumeAsync(CancellationToken cancellationToken = default);
}