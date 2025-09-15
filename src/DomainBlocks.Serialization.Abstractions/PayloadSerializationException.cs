using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.Serialization.Abstractions;

public class PayloadSerializationException : DomainBlocksException
{
    public PayloadSerializationException()
    {
    }

    public PayloadSerializationException(string? message) : base(message)
    {
    }

    public PayloadSerializationException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    public static PayloadSerializationException ForSerialization(Type type, Exception innerException) =>
        new($"Serialization failed for type '{type.FullName}'.", innerException);

    public static PayloadSerializationException ForDeserialization(Type type, Exception innerException) =>
        new($"Deserialization failed for type '{type.FullName}'.", innerException);

    public static PayloadSerializationException NullResult(Type type) =>
        new($"Payload deserialized to null for type '{type.FullName}'.");
}