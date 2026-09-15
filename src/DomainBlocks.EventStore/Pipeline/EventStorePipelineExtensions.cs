using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.Pipeline;

public static class EventStorePipelineExtensions
{
    extension<TEvent, TStreamId, TStreamPos, TLogPos>(
        IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> eventStore)
        where TEvent : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        /// <summary>
        /// Wraps the store in a pipeline of domain-facing stages: metadata contributors run on append, and read
        /// transforms run on reads and subscriptions. Stages that are not configured cost nothing, and a pipeline with
        /// no stages returns the store itself. The result forwards <see cref="IAsyncDisposable"/> to the store.
        /// </summary>
        public IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> WithPipeline(
            Action<EventStorePipelineBuilder<TEvent>> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            var builder = new EventStorePipelineBuilder<TEvent>();
            configure(builder);

            return builder.Build(eventStore);
        }
    }
}