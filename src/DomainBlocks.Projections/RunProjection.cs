using System.Threading.Tasks;

namespace DomainBlocks.Projections
{
    public delegate Task RunProjection(object @event);
}