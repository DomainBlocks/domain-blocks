using DomainBlocks.Serialization.Abstractions;
using Google.Protobuf;

namespace DomainBlocks.Serialization.Google.Protobuf;

public sealed class ProtobufBytesSerializer :
    IObjectSerializer<byte[]>,
    IObjectSerializer<ReadOnlyMemory<byte>>,
    IContentTypeSource
{
    public string ContentType => "application/protobuf";

    public byte[] Serialize(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value is not IMessage message)
            throw new ArgumentException("Object must implement IMessage.", nameof(value));

        using var ms = new MemoryStream();
        message.WriteTo(ms);
        return ms.ToArray();
    }

    public object Deserialize(byte[] value, Type type) => Deserialize(value.AsSpan(), type);

    ReadOnlyMemory<byte> IObjectSerializer<ReadOnlyMemory<byte>>.Serialize(object value) => Serialize(value);

    object IObjectSerializer<ReadOnlyMemory<byte>>.Deserialize(ReadOnlyMemory<byte> value, Type type) =>
        Deserialize(value.Span, type);

    private static object Deserialize(ReadOnlySpan<byte> value, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!typeof(IMessage).IsAssignableFrom(type))
            throw new ArgumentException("Type must implement IMessage.", nameof(type));

        var parser = MessageParserCache.Get(type);

        return parser.ParseFrom(value);
    }
}