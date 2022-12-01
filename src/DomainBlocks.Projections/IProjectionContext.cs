using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Projections.New;

namespace DomainBlocks.Projections;

public interface IProjectionContext
{
    Task OnInitializing(CancellationToken cancellationToken = default);
    Task<IStreamPosition> OnSubscribing(CancellationToken cancellationToken = default);
    Task OnCatchingUp(CancellationToken cancellationToken = default);
    Task OnCaughtUp(IStreamPosition position, CancellationToken cancellationToken = default);
    Task OnEventDispatching(CancellationToken cancellationToken = default);
    Task OnEventHandled(IStreamPosition position, CancellationToken cancellationToken = default);
}