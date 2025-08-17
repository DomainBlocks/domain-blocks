using DomainBlocks.Serialization.Abstractions;
using Google.Protobuf;

namespace DomainBlocks.Serialization.Google.Protobuf;

public class GoogleProtobufBytesSerializer : ISerializer<ReadOnlyMemory<byte>>
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
}