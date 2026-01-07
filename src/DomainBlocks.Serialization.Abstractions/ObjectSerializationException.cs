using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.Serialization.Abstractions;

public class ObjectSerializationException(string? message = null, Exception? innerException = null) :
    DomainBlocksException(message, innerException)
{
    public static ObjectSerializationException SerializationFailed(Type type, Exception innerException) =>
        new($"Serialization failed for type '{type.FullName}'.", innerException);

    public static ObjectSerializationException DeserializationFailed(Type type, Exception innerException) =>
        new($"Deserialization failed for type '{type.FullName}'.", innerException);

    public static ObjectSerializationException DeserializationReturnedNull(Type type) =>
        new($"Value deserialized to null for type '{type.FullName}'.");
}