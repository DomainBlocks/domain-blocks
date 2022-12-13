using System;
using System.Collections.Concurrent;
using DomainBlocks.Core;
using DomainBlocks.Serialization;
using DomainBlocks.Serialization.Json;
using SqlStreamStore;

namespace DomainBlocks.Persistence.SqlStreamStore;

public class SqlStreamStoreOptions
{
    private static readonly Func<IEventNameMap, IEventSerializer<string>> DefaultEventSerializerFactory =
        eventNameMap => new JsonStringEventSerializer(eventNameMap);

    private Lazy<IStreamStore> _streamStore;
    private Func<IEventNameMap, IEventSerializer<string>> _eventSerializerFactory;
    private ConcurrentDictionary<IEventNameMap, IEventSerializer<string>> _eventSerializers = new();

    public SqlStreamStoreOptions()
    {
    }

    private SqlStreamStoreOptions(SqlStreamStoreOptions copyFrom)
    {
        _streamStore = copyFrom._streamStore;
        _eventSerializerFactory = copyFrom._eventSerializerFactory ?? DefaultEventSerializerFactory;
        _eventSerializers = copyFrom._eventSerializers;
    }

    public SqlStreamStoreOptions WithStreamStoreFactory(Func<IStreamStore> factory)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));

        return new SqlStreamStoreOptions(this) { _streamStore = new Lazy<IStreamStore>(factory) };
    }

    public SqlStreamStoreOptions WithEventSerializerFactory(Func<IEventNameMap, IEventSerializer<string>> factory)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));

        return new SqlStreamStoreOptions(this)
        {
            _eventSerializerFactory = factory,

            // As the factory has changed, we should also clear any existing cached serializers.
            _eventSerializers = new ConcurrentDictionary<IEventNameMap, IEventSerializer<string>>()
        };
    }

    public IStreamStore GetOrCreateStreamStore()
    {
        if (_streamStore == null)
        {
            throw new InvalidOperationException("Cannot create stream store as no factory has been specified.");
        }

        return _streamStore.Value;
    }

    public IEventSerializer<string> GetOrCreateEventSerializer(IEventNameMap eventNameMap)
    {
        if (eventNameMap == null) throw new ArgumentNullException(nameof(eventNameMap));

        return _eventSerializers.GetOrAdd(eventNameMap, _eventSerializerFactory);
    }
}