using DomainBlocks.Serialization.Abstractions;
using Google.Protobuf;

namespace DomainBlocks.Serialization.Google.Protobuf;

public class GoogleProtobufBytesSerializer : ISerializer<ReadOnlyMemory<byte>>, ISerializer<byte[]>
{
    public ReadOnlyMemory<byte> Serialize(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value is not IMessage message)
            throw new ArgumentException("Object must implement IMessage.", nameof(value));

        using var ms = new MemoryStream();
        message.WriteTo(ms);
        return ms.ToArray();
    }

    public object? Deserialize(ReadOnlyMemory<byte> payload, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!typeof(IMessage).IsAssignableFrom(type))
            throw new ArgumentException("Type must implement IMessage.", nameof(type));

        var parser = MessageParserCache.Get(type);

        return parser.ParseFrom(payload.ToArray());
    }

    byte[] ISerializer<byte[]>.Serialize(object value) => Serialize(value).ToArray();

    object? ISerializer<byte[]>.Deserialize(byte[] payload, Type type) => Deserialize(payload, type);
}