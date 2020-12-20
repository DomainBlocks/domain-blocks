using System.Threading.Tasks;

namespace DomainLib.Projections
{
    public delegate Task RunProjection(object @event);
}