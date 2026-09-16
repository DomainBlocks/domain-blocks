using DomainBlocks.Serialization.Abstractions;
using Google.Protobuf;

namespace DomainBlocks.Serialization.Google.Protobuf;

public sealed class ProtobufBytesObjectSerializer : IByteObjectSerializer
{
    public byte[] Serialize(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value is not IMessage message)
            throw new ArgumentException("Object must implement IMessage.", nameof(value));

        using var ms = new MemoryStream();
        message.WriteTo(ms);
        return ms.ToArray();
    }

    public object Deserialize(ReadOnlySpan<byte> data, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!typeof(IMessage).IsAssignableFrom(type))
            throw new ArgumentException("Type must implement IMessage.", nameof(type));

        var parser = MessageParserCache.Get(type);

        return parser.ParseFrom(data);
    }
}