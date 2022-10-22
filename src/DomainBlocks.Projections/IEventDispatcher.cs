using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections;

public interface IEventDispatcher
{
    public Task StartAsync(CancellationToken cancellationToken = default);
}