using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Core.Metadata;

namespace DomainBlocks.Projections;

public delegate Task RunProjection(object @event, EventMetadata metadata, CancellationToken cancellationToken);