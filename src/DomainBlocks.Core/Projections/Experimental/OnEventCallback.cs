namespace DomainBlocks.Core.Projections.Experimental;

public delegate Task OnEventCallback<in TEvent>(
    TEvent @event,
    IReadOnlyDictionary<string, string> metadata,
    CancellationToken cancellationToken);

public delegate Task OnEventCallback<in TEvent, in TState>(
    TEvent @event,
    IReadOnlyDictionary<string, string> metadata,
    TState state,
    CancellationToken cancellationToken);