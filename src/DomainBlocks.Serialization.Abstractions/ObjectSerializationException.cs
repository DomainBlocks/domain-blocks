using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.Serialization.Abstractions;

public class ObjectSerializationException : DomainBlocksException
{
    public ObjectSerializationException()
    {
    }

    public ObjectSerializationException(string? message) : base(message)
    {
    }

    public ObjectSerializationException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    public static ObjectSerializationException ForSerialization(Type type, Exception innerException) =>
        new($"Serialization failed for type '{type.FullName}'.", innerException);

    public static ObjectSerializationException ForDeserialization(Type type, Exception innerException) =>
        new($"Deserialization failed for type '{type.FullName}'.", innerException);

    public static ObjectSerializationException NullResult(Type type) =>
        new($"Value deserialized to null for type '{type.FullName}'.");
}