using System;
using DomainBlocks.Serialization;
using SqlStreamStore;

namespace DomainBlocks.Projections.SqlStreamStore.New;

public class SqlStreamStoreOptions
{
    private static readonly Func<IEventDeserializer<StreamMessageWrapper>> DefaultEventDeserializerFactory =
        () => new StreamMessageJsonDeserializer();

    public SqlStreamStoreOptions()
    {
    }

    private SqlStreamStoreOptions(SqlStreamStoreOptions copyFrom)
    {
        StreamStoreFactory = copyFrom.StreamStoreFactory;
        EventDeserializerFactory = copyFrom.EventDeserializerFactory ?? DefaultEventDeserializerFactory;
    }

    public Func<IStreamStore> StreamStoreFactory { get; private init; }
    public Func<IEventDeserializer<StreamMessageWrapper>> EventDeserializerFactory { get; private init; }

    public SqlStreamStoreOptions WithStreamStoreFactory(Func<IStreamStore> streamStoreFactory)
    {
        return new SqlStreamStoreOptions(this) { StreamStoreFactory = streamStoreFactory };
    }

    public SqlStreamStoreOptions WithEventDeserializerFactory(
        Func<IEventDeserializer<StreamMessageWrapper>> eventDeserializerFactory)
    {
        return new SqlStreamStoreOptions(this) { EventDeserializerFactory = eventDeserializerFactory };
    }
}