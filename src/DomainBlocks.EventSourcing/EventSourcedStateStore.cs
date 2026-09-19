using DomainBlocks.Core;
using DomainBlocks.EventStore;

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

        ExpectedStreamState<TStreamPos> expectedStreamState = expectedVersion.HasValue
            ? expectedVersion.Value
            : ExpectedStreamState.DoesNotExist;

        try
        {
            await eventStore
                .AppendAsync(streamId, uncommittedEvents, expectedStreamState, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (StreamAppendConflictException<TStreamPos> ex)
        {
            // Null when the store did not observe the stream. TStreamPos is a type parameter here, so the version is
            // read with TryGetValue: a pattern cannot bind a variable to a union case that is a type parameter.
            Optional<TStreamPos>? observedVersion = null;

            if (ex.ObservedState.TryGetValue(out TStreamPos? version))
                observedVersion = Optional.From(version);
            else if (ex.ObservedState is StreamDoesNotExist)
                observedVersion = Optional.None<TStreamPos>();

            throw new VersionConflictException<TStreamPos>(streamId, expectedVersion, observedVersion, ex);
        }
    }

    public Task SaveNewAsync(TState state, CancellationToken cancellationToken = default)
    {
        return SaveAsync(state, Optional.None<TStreamPos>(), cancellationToken);
    }
}