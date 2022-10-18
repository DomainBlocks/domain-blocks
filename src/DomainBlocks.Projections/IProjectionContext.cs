using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections;

public interface IProjectionContext
{
    Task OnInitializing(CancellationToken cancellationToken = default);
    Task OnCaughtUp(CancellationToken cancellationToken = default);
    Task OnEventDispatching(CancellationToken cancellationToken = default);
    Task OnEventHandled(CancellationToken cancellationToken = default);
}