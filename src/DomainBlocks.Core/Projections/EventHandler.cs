namespace DomainBlocks.Core.Projections;

public delegate Task EventHandler<in TEvent, in TState>(
    IEventRecord<TEvent> eventRecord,
    TState state,
    CancellationToken cancellationToken);