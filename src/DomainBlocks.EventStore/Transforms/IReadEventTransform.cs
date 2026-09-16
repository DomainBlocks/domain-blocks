namespace DomainBlocks.EventStore.Transforms;

/// <summary>
/// Reshapes events of one source type as they are read, for example to upcast a retired event into its current
/// form or to split one legacy event into several. Applied by a store pipeline to reads and subscriptions alike.
/// Derived events keep the source event's <see cref="ReadEventContext{TStreamId, TStreamPos, TLogPos}"/>, and are
/// themselves run through the pipeline's transforms, so transforms chain.
/// </summary>
/// <typeparam name="TEvent">The event base type of the store.</typeparam>
public interface IReadEventTransform<TEvent> where TEvent : notnull
{
    /// <summary>
    /// The exact type of event this transform applies to.
    /// </summary>
    Type SourceEventType { get; }

    /// <summary>
    /// Produces the events that replace <paramref name="event"/>. Returning no events drops the source event, which a
    /// pipeline rejects unless it has been configured to allow it.
    /// </summary>
    IEnumerable<TEvent> Apply<TStreamId, TStreamPos, TLogPos>(
        TEvent @event,
        in ReadEventContext<TStreamId, TStreamPos, TLogPos> context)
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull;
}