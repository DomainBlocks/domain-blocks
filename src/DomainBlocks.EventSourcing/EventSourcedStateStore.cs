using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventSourcing;

public sealed class EventSourcedStateStore<TEvent, TStreamId, TStreamPos, TLogPos>(
    IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> eventStore,
    IEventSourcedStateAdapterResolver<TEvent, TStreamId> adapterResolver) :
    IEventSourcedStateStore<TStreamId, TStreamPos>
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    public async Task<(TState State, Optional<TStreamPos> Version)> LoadAsync<TState>(
        TStreamId streamId,
        CancellationToken cancellationToken = default)
        where TState : notnull
    {
        var adapter = GetRequiredAdapter<TState>();
        var initialState = adapter.CreateInitialState();

        Optional<TStreamPos> loadedVersion = default;

        var loadedState = await adapter
            .LoadAsync(initialState, EnumerateEvents(), cancellationToken)
            .ConfigureAwait(false);

        return (loadedState, loadedVersion);

        async IAsyncEnumerable<TEvent> EnumerateEvents()
        {
            await foreach (var e in eventStore
                               .ReadStream(streamId)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                loadedVersion = e.Context.StreamPosition;
                yield return e.Payload;
            }
        }
    }

    public async Task<(TState State, TStreamPos Version)> LoadRequiredAsync<TState>(
        TStreamId streamId,
        CancellationToken cancellationToken = default)
        where TState : notnull
    {
        var (state, version) = await LoadAsync<TState>(streamId, cancellationToken);
        return version.HasValue ? (state, version.Value) : throw new StreamNotFoundException(streamId);
    }

    public async Task SaveAsync<TState>(
        TState state,
        Optional<TStreamPos> expectedVersion,
        CancellationToken cancellationToken = default)
        where TState : notnull
    {
        ArgumentNullException.ThrowIfNull(state);

        var adapter = GetRequiredAdapter<TState>();
        var streamId = adapter.GetStreamId(state);

        // PoC for adding metadata.
        // Should this be stream-level metadata?
        KeyValuePair<string, string>[] metadata = [KeyValuePair.Create("ClrType", state.GetType().Name)];

        var uncommittedEvents = adapter.GetUncommittedEvents(state)
            .Select(e => AppendableEvent.Create(e, metadata))
            .ToArray();

        if (uncommittedEvents.Length == 0)
            return;

        var expectedStreamState = expectedVersion.HasValue
            ? ExpectedStreamState.AtVersion(expectedVersion.Value)
            : ExpectedStreamState.DoesNotExist<TStreamPos>();

        await eventStore
            .AppendAsync(streamId, uncommittedEvents, expectedStreamState, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public Task SaveNewAsync<TState>(TState state, CancellationToken cancellationToken = default) where TState : notnull
    {
        return SaveAsync(state, Optional.None<TStreamPos>(), cancellationToken);
    }

    private IEventSourcedStateAdapter<TState, TEvent, TStreamId> GetRequiredAdapter<TState>()
        where TState : notnull
    {
        return adapterResolver.Resolve<TState>() ?? throw new ArgumentException(
            $"Event sourced state adapter not found for type '{typeof(TState)}'.",
            nameof(TState));
    }
}