using System.Reflection;
using DomainBlocks.Serialization.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace DomainBlocks.Serialization.MongoDB.Bson;

public sealed class BsonDocumentObjectSerializer : IObjectSerializer<BsonDocument>, IObjectSerializer<BsonValue>
{
    public BsonDocument Serialize(object value) => value.ToBsonDocument(value.GetType());

    public object Deserialize(BsonDocument data, Type type) => BsonSerializer.Deserialize(data, type);

    BsonValue IObjectSerializer<BsonValue>.Serialize(object value) => Serialize(value);

    object IObjectSerializer<BsonValue>.Deserialize(BsonValue data, Type type) =>
        Deserialize(data.AsBsonDocument, type);

    /// <summary>
    /// The name of the element, for a type that is written as a document of its members, and a value that a
    /// serializer of the driver writes and reads back. What a serializer of the caller's writes is for it alone to
    /// read, and a value that is written but not read back says nothing of the object that is read.
    /// </summary>
    public string? GetStoredName(Type type, MemberInfo member)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);

        // A dictionary is written as a document too, but of its keys.
        if (BsonSerializer.LookupSerializer(type) is not IBsonDocumentSerializer ofMembers ||
            ofMembers.GetType() is not { IsGenericType: true } serializerType ||
            serializerType.GetGenericTypeDefinition() != typeof(BsonClassMapSerializer<>) ||
            !ofMembers.TryGetMemberSerializationInfo(member.Name, out var info))
        {
            return null;
        }

        // One that a constructor reads back counts as not read back too, which costs a narrower query and no more.
        if (BsonClassMap.LookupClassMap(type).GetMemberMap(member.Name) is not { IsReadOnly: false })
            return null;

        // The serializer of a nullable is the driver's own, whichever serializer writes the value inside it.
        var valueSerializer = info.Serializer;

        while (valueSerializer is IChildSerializerConfigurable { ChildSerializer: { } child } &&
               Nullable.GetUnderlyingType(valueSerializer.ValueType) is not null)
        {
            valueSerializer = child;
        }

        if (valueSerializer.GetType().Assembly != typeof(BsonSerializer).Assembly)
            return null;

        // A number that is written as a double is not the number that it is read back as: 9.99 is stored as a little
        // more, and a database compares it so. As text it compares with no number, so nothing is lost by looking.
        return valueSerializer is IRepresentationConfigurable { Representation: BsonType.Double }
            ? null
            : info.ElementName;
    }
}