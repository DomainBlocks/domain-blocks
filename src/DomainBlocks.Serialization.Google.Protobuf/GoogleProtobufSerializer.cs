using System.Collections.Concurrent;
using System.Reflection;
using DomainBlocks.Serialization.Abstractions;
using Google.Protobuf;

namespace DomainBlocks.Serialization.Google.Protobuf;

public class GoogleProtobufSerializer : ISerializer<ReadOnlyMemory<byte>>
{
    private static readonly ConcurrentDictionary<Type, MessageParser> Parsers = new();

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

        var parser = Parsers.GetOrAdd(type, GetParser);

        return parser.ParseFrom(payload.ToArray());
    }

    private static MessageParser GetParser(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;

        var parserProperty = type.GetProperty("Parser", flags);

        if (parserProperty == null || !typeof(MessageParser).IsAssignableFrom(parserProperty.PropertyType))
        {
            throw new ArgumentException(
                $"Type '{type.FullName}' does not expose a valid static Parser property.",
                nameof(type));
        }

        return (MessageParser)parserProperty.GetValue(null)!;
    }
}