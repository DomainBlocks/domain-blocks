using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Events;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore;

public sealed class EventStoreClient<TEventBase, TSerialized> :
    IEventStoreClient<TEventBase>
    where TEventBase : class
    where TSerialized : notnull
{
    private readonly Func<CancellationToken, ValueTask<IEventStoreClientAdapter<TSerialized>>> _adapterFactory;
    private readonly EventTypeMap _eventTypeMap;
    private readonly IObjectSerializer<TSerialized> _serializer;
    private readonly FrozenDictionary<Type, IEventContractMapper<TEventBase>> _contractMappersByEventType;
    private readonly FrozenDictionary<Type, IEventContractMapper<TEventBase>> _contractMappersByContractType;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly Lock _lock = new();
    private volatile IEventStoreClientAdapter<TSerialized>? _adapter;
    private Task<IEventStoreClientAdapter<TSerialized>>? _adapterCreateTask;

    private int _disposed;

    public EventStoreClient(EventStoreClientOptions<TEventBase, TSerialized> options)
    {
        _adapterFactory = options.AdapterFactory;
        _eventTypeMap = options.TypeMap;
        _serializer = options.Serializer;
        _contractMappersByEventType = options.ContractMappers.ToFrozenDictionary(x => x.EventType);
        _contractMappersByContractType = options.ContractMappers.ToFrozenDictionary(x => x.ContractType);
    }

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<UncommittedEvent<TEventBase>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var serializedEvents = events
            .Select(e =>
            {
                string eventName;
                var payload = e.Payload;
                object payloadToSerialize = payload;

                if (_contractMappersByEventType.TryGetValue(payload.GetType(), out var mapper))
                {
                    eventName = _eventTypeMap.GetEventName(mapper.ContractType);
                    payloadToSerialize = mapper.ToContract(payload);
                }
                else
                {
                    eventName = _eventTypeMap.GetEventName(payload.GetType());
                }

                // PoC for adding metadata.
                var header = e.Header
                    .WithEventName(eventName)
                    .WithMetadata("EventClrType", payloadToSerialize.GetType().Name);

                var serializedPayload = _serializer.Serialize(payloadToSerialize);

                return UncommittedEvent.Create(header, serializedPayload);
            });

        var adapter = await GetAdapterAsync(cancellationToken).ConfigureAwait(false);

        await adapter
            .AppendToStreamAsync(streamId, serializedEvents, options, cancellationToken)
            .ConfigureAwait(false);
    }

    public async IAsyncEnumerable<CommittedEvent<TEventBase>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var adapter = await GetAdapterAsync(cancellationToken).ConfigureAwait(false);
        var serializedEvents = adapter.ReadStreamAsync(streamId, options, cancellationToken);

        await foreach (var serializedEvent in serializedEvents.ConfigureAwait(false))
        {
            var header = serializedEvent.Header;
            var eventType = _eventTypeMap.GetEventType(header.EventName);
            var deserializedPayload = _serializer.Deserialize(serializedEvent.Payload, eventType);

            if (_contractMappersByContractType.TryGetValue(deserializedPayload.GetType(), out var mapper))
                deserializedPayload = mapper.FromContract(deserializedPayload);

            yield return CommittedEvent.Create(header, (TEventBase)deserializedPayload);
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

        Task<IEventStoreClientAdapter<TSerialized>>? createTask;
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

    private ValueTask<IEventStoreClientAdapter<TSerialized>> GetAdapterAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        var adapter = _adapter;
        if (adapter is not null)
            return ValueTask.FromResult(adapter);

        Task<IEventStoreClientAdapter<TSerialized>> createTask;

        lock (_lock)
        {
            ThrowIfDisposed();

            // Ensure only one initialization runs; others await the same task.
            createTask = (_adapterCreateTask ??= CreateAdapterAsync()).WaitAsync(cancellationToken);
        }

        return new ValueTask<IEventStoreClientAdapter<TSerialized>>(createTask);
    }

    private async Task<IEventStoreClientAdapter<TSerialized>> CreateAdapterAsync()
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

    private static ValueTask DisposeIfSupportedAsync(IEventStoreClientAdapter<TSerialized> adapter)
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