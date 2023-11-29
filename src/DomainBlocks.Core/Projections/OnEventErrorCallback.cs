using DomainBlocks.Core.Subscriptions;

namespace DomainBlocks.Core.Projections;

public delegate Task<EventErrorResolution> OnEventErrorCallback<TRawEvent, TPosition>(
    EventError<TRawEvent, TPosition> eventError,
    CancellationToken cancellationToken);

public delegate Task<EventErrorResolution> OnEventErrorCallback<TRawEvent, TPosition, in TState>(
    EventError<TRawEvent, TPosition> eventError,
    TState state,
    CancellationToken cancellationToken);