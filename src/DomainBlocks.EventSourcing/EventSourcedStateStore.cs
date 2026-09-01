using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventSourcing;

public static class EventSourcedStateStore
{
    public static EventSourcedStateStore<TState, TEvent, TStreamId, TStreamPos, TLogPos>
        Create<TState, TEvent, TStreamId, TStreamPos, TLogPos>(
            IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> eventStore,
            IEventSourcedStateAdapter<TState, TEvent, TStreamId> adapter)
        where TState : notnull
        where TEvent : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        return new EventSourcedStateStore<TState, TEvent, TStreamId, TStreamPos, TLogPos>(eventStore, adapter);
    }
}

public sealed class EventSourcedStateStore<TState, TEvent, TStreamId, TStreamPos, TLogPos>(
    IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> eventStore,
    IEventSourcedStateAdapter<TState, TEvent, TStreamId> adapter) :
    IVersionedStateStore<TState, TStreamId, TStreamPos>
    where TState : notnull
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    public async Task<(TState State, Optional<TStreamPos> Version)> LoadAsync(
        TStreamId streamId,
        CancellationToken cancellationToken = default)
    {
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

    public async Task<(TState State, TStreamPos Version)> LoadRequiredAsync(
        TStreamId streamId,
        CancellationToken cancellationToken = default)
    {
        var (state, version) = await LoadAsync(streamId, cancellationToken).ConfigureAwait(false);
        return version.HasValue ? (state, version.Value) : throw new StateNotFoundException(streamId);
    }

    public async Task SaveAsync(
        TState state,
        Optional<TStreamPos> expectedVersion,
        CancellationToken cancellationToken = default)
    {
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

        try
        {
            await eventStore
                .AppendAsync(streamId, uncommittedEvents, expectedStreamState, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (StreamAppendConflictException<TStreamPos> ex)
        {
            Optional<TStreamPos>? observedVersion = ex.ObservedState is { HasVersion: true }
                ? ex.ObservedState.Value.Version
                : null;

            throw new VersionConflictException<TStreamPos>(streamId, expectedVersion, observedVersion, ex);
        }
    }

    public Task SaveNewAsync(TState state, CancellationToken cancellationToken = default)
    {
        return SaveAsync(state, Optional.None<TStreamPos>(), cancellationToken);
    }
}