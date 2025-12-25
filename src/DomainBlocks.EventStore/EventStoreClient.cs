using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Events;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore;

public sealed class EventStoreClient<TPayload> : IEventStoreClient where TPayload : notnull
{
    private readonly Func<CancellationToken, ValueTask<IEventStoreAdapter<TPayload>>> _adapterFactory;
    private readonly EventTypeMap _eventTypeMap;
    private readonly IPayloadSerializer<TPayload> _serializer;
    private readonly FrozenDictionary<Type, IEventContractMapper> _contractMappersByEventType;
    private readonly FrozenDictionary<Type, IEventContractMapper> _contractMappersByContractType;
    private readonly FrozenDictionary<Type, IEventReadTransform> _readTransforms;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly Lock _lock = new();
    private volatile IEventStoreAdapter<TPayload>? _adapter;
    private Task<IEventStoreAdapter<TPayload>>? _adapterCreateTask;

    private int _disposed;

    public EventStoreClient(EventStoreClientOptions<TPayload> options)
    {
        _adapterFactory = options.AdapterFactory;
        _eventTypeMap = options.TypeMap;
        _serializer = options.Serializer;
        _contractMappersByEventType = options.ContractMappers.ToFrozenDictionary(x => x.EventType);
        _contractMappersByContractType = options.ContractMappers.ToFrozenDictionary(x => x.ContractType);
        _readTransforms = options.ReadTransforms.ToFrozenDictionary(x => x.FromType);
    }

    public Task AppendToStreamAsync(
        string streamId,
        IEnumerable<object> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return AppendToStreamAsync(streamId, events.Select(UncommittedEvent.Create), options, cancellationToken);
    }

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<UncommittedEvent<object>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var serializedEvents = events
            .Select(e =>
            {
                string eventName;
                var payload = e.Payload;

                if (_contractMappersByEventType.TryGetValue(payload.GetType(), out var mapper))
                {
                    eventName = _eventTypeMap.GetEventName(mapper.ContractType);
                    payload = mapper.ToContract(payload);
                }
                else
                {
                    eventName = _eventTypeMap.GetEventName(payload.GetType());
                }

                // PoC for adding metadata.
                var header = e.Header
                    .WithEventName(eventName)
                    .WithMetadata("EventClrType", payload.GetType().Name);

                var serializedPayload = _serializer.Serialize(payload);

                return UncommittedEvent.Create(header, serializedPayload);
            });

        var adapter = await GetAdapterAsync(cancellationToken).ConfigureAwait(false);

        await adapter
            .AppendToStreamAsync(streamId, serializedEvents, options, cancellationToken)
            .ConfigureAwait(false);
    }

    public async IAsyncEnumerable<CommittedEvent<object>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var adapter = await GetAdapterAsync(cancellationToken).ConfigureAwait(false);
        var serializedEvents = adapter.ReadStreamAsync(streamId, options, cancellationToken);
        var queue = _readTransforms.Count > 0 ? new Queue<object>() : null;

        await foreach (var serializedEvent in serializedEvents.ConfigureAwait(false))
        {
            var header = serializedEvent.Header;
            var eventType = _eventTypeMap.GetEventType(header.EventName);
            var deserializedPayload = _serializer.Deserialize(serializedEvent.Payload, eventType);

            if (_contractMappersByContractType.TryGetValue(deserializedPayload.GetType(), out var mapper))
                deserializedPayload = mapper.FromContract(deserializedPayload);

            if (queue == null)
            {
                yield return CommittedEvent.Create(header, deserializedPayload);
                continue;
            }

            queue.Enqueue(deserializedPayload);

            while (queue.TryDequeue(out var @event))
            {
                if (_readTransforms.TryGetValue(@event.GetType(), out var transform))
                {
                    var transformedEvents = transform.Apply(@event, header);

                    foreach (var transformedEvent in transformedEvents)
                        queue.Enqueue(transformedEvent);
                }
                else
                {
                    yield return CommittedEvent.Create(header, @event);
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _lifetimeCts.CancelAsync().ConfigureAwait(false);

        // Fast path: if adapter already created, dispose it.
        var adapter = _adapter;
        if (adapter is not null)
        {
            await DisposeIfSupportedAsync(adapter).ConfigureAwait(false);
            _lifetimeCts.Dispose();
            return;
        }

        Task<IEventStoreAdapter<TPayload>>? createTask;
        lock (_lock)
            createTask = _adapterCreateTask;

        if (createTask is not null)
        {
            try
            {
                adapter = await createTask.ConfigureAwait(false);
                await DisposeIfSupportedAsync(adapter).ConfigureAwait(false);
            }
            catch
            {
                // Create task failed/canceled; nothing to dispose.
            }
        }

        _lifetimeCts.Dispose();
    }

    private ValueTask<IEventStoreAdapter<TPayload>> GetAdapterAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        var adapter = _adapter;
        if (adapter is not null)
            return ValueTask.FromResult(adapter);

        Task<IEventStoreAdapter<TPayload>> createTask;

        lock (_lock)
        {
            ThrowIfDisposed();

            // Ensure only one initialization runs; others await the same task.
            createTask = (_adapterCreateTask ??= CreateAdapterAsync()).WaitAsync(cancellationToken);
        }

        return new ValueTask<IEventStoreAdapter<TPayload>>(createTask);
    }

    private async Task<IEventStoreAdapter<TPayload>> CreateAdapterAsync()
    {
        try
        {
            var adapter = await _adapterFactory(_lifetimeCts.Token).ConfigureAwait(false);
            _adapter = adapter;
            return adapter;
        }
        catch
        {
            // Allow retry if create fails.
            lock (_lock)
                _adapterCreateTask = null;

            throw;
        }
    }

    private static ValueTask DisposeIfSupportedAsync(IEventStoreAdapter<TPayload> adapter)
    {
        if (adapter is IAsyncDisposable asyncDisposable)
            return asyncDisposable.DisposeAsync();

        // ReSharper disable once SuspiciousTypeConversion.Global
        // Implementers outside of the library codebase may be IDisposable.
        if (adapter is IDisposable disposable)
            disposable.Dispose();

        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed != 0, this);
}