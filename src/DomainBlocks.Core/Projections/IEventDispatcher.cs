namespace DomainBlocks.Core.Projections;

public interface IEventDispatcher
{
    public Task StartAsync(CancellationToken cancellationToken = default);
}