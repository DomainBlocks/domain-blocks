using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventSourcing;

public sealed class EventSourcedStateStore<TEvent, TStreamId, TStreamPos, TLogPos>(
    IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> eventStore,
    IEventSourcedStateDefinitionProvider<TEvent, TStreamId> definitionProvider) :
    IEventSourcedStateStore<TStreamId, TStreamPos>
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    public async Task<EventSourcedState<TState, TStreamPos>> LoadAsync<TState>(
        TStreamId streamId,
        CancellationToken cancellationToken = default)
        where TState : notnull
    {
        return await LoadCoreAsync<TState>(streamId, StreamNotFoundBehavior.Throw, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<EventSourcedState<TState, TStreamPos>> LoadOrCreateAsync<TState>(
        TStreamId streamId,
        CancellationToken cancellationToken = default)
        where TState : notnull
    {
        return await LoadCoreAsync<TState>(streamId, StreamNotFoundBehavior.Ignore, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SaveAsync<TState>(
        EventSourcedState<TState, TStreamPos> state,
        CancellationToken cancellationToken = default)
        where TState : notnull
    {
        ArgumentNullException.ThrowIfNull(state);

        var definition = GetRequiredDefinition<TState>();
        var streamId = definition.GetStreamId(state.Value);

        // PoC for adding metadata.
        // Should this be stream-level metadata?
        KeyValuePair<string, string>[] metadata = [KeyValuePair.Create("ClrType", state.Value.GetType().Name)];

        var uncommittedEvents = definition.GetUncommittedEvents(state.Value)
            .Select(e => AppendableEvent.Create(e, metadata))
            .ToArray();

        if (uncommittedEvents.Length == 0)
            return;

        var expectedStreamState = state.Version.HasValue
            ? ExpectedStreamState<TStreamPos>.AtVersion(state.Version.Value)
            : ExpectedStreamState<TStreamPos>.DoesNotExist;

        await eventStore
            .AppendAsync(streamId, uncommittedEvents, expectedStreamState, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<EventSourcedState<TState, TStreamPos>> LoadCoreAsync<TState>(
        TStreamId streamId,
        StreamNotFoundBehavior streamNotFoundBehavior,
        CancellationToken cancellationToken)
        where TState : notnull
    {
        var definition = GetRequiredDefinition<TState>();
        var initialState = definition.CreateInitialState();

        Optional<TStreamPos> loadedVersion = default;

        var state = await definition
            .RestoreAsync(initialState, EnumerateEvents(), cancellationToken)
            .ConfigureAwait(false);

        return new EventSourcedState<TState, TStreamPos>(state, loadedVersion);

        async IAsyncEnumerable<TEvent> EnumerateEvents()
        {
            var readOptions = new ReadStreamOptions { StreamNotFoundBehavior = streamNotFoundBehavior };

            await foreach (var e in eventStore
                               .ReadStream(streamId, options: readOptions)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                loadedVersion = e.Context.StreamPosition;
                yield return e.Payload;
            }
        }
    }

    private IEventSourcedStateDefinition<TState, TEvent, TStreamId> GetRequiredDefinition<TState>()
        where TState : notnull
    {
        return definitionProvider.GetDefinition<TState>() ?? throw new ArgumentException(
            $"Event sourced state definition not found for type '{typeof(TState)}'.",
            nameof(TState));
    }
}