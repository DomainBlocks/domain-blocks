using DomainBlocks.Core.Serialization;
using SqlStreamStore;

namespace DomainBlocks.SqlStreamStore;

public class SqlStreamStoreOptions
{
    private static readonly Func<IEventDataSerializer<string>> DefaultEventDataSerializerFactory =
        () => new JsonStringEventDataSerializer();

    private Lazy<IStreamStore>? _streamStore;
    private Func<IEventDataSerializer<string>>? _eventSerializerFactory;

    public SqlStreamStoreOptions()
    {
    }

    private SqlStreamStoreOptions(SqlStreamStoreOptions copyFrom)
    {
        _streamStore = copyFrom._streamStore;
        _eventSerializerFactory = copyFrom._eventSerializerFactory ?? DefaultEventDataSerializerFactory;
    }

    public SqlStreamStoreOptions WithStreamStoreFactory(Func<IStreamStore> factory)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));

        return new SqlStreamStoreOptions(this) { _streamStore = new Lazy<IStreamStore>(factory) };
    }

    public SqlStreamStoreOptions WithEventDataSerializerFactory(Func<IEventDataSerializer<string>> factory)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));

        return new SqlStreamStoreOptions(this) { _eventSerializerFactory = factory };
    }

    public IStreamStore GetOrCreateStreamStore()
    {
        if (_streamStore == null)
        {
            throw new InvalidOperationException("Cannot create stream store as no factory has been specified.");
        }

        return _streamStore.Value;
    }

    public IEventDataSerializer<string> GetEventDataSerializer() => _eventSerializerFactory!();
}