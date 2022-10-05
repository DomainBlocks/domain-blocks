using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections;

public interface IProjectionContext
{
    Task OnSubscribing(CancellationToken cancellationToken = default);
    Task OnCaughtUp(CancellationToken cancellationToken = default);
    Task OnBeforeHandleEvent(CancellationToken cancellationToken = default);
    Task OnAfterHandleEvent(CancellationToken cancellationToken = default);
}