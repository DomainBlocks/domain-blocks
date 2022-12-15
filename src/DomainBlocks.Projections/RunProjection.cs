using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Serialization;

namespace DomainBlocks.Projections;

public delegate Task RunProjection(object @event, EventMetadata metadata, CancellationToken cancellationToken);