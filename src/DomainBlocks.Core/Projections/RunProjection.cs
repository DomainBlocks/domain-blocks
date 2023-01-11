using DomainBlocks.Core.Metadata;

namespace DomainBlocks.Core.Projections;

public delegate Task RunProjection(object @event, EventMetadata metadata, CancellationToken cancellationToken);