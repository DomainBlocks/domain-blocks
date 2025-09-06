using DomainBlocks.Serialization.Abstractions;
using Google.Protobuf;

namespace DomainBlocks.Serialization.Google.Protobuf;

public sealed class GoogleProtobufJsonStringSerializer(JsonFormatter.Settings? settings = null) : IPayloadSerializer<string>
{
    private readonly JsonFormatter _formatter = new(settings ?? JsonFormatter.Settings.Default);

    public string Serialize(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value is not IMessage message)
            throw new ArgumentException("Object must implement IMessage.", nameof(value));

        return _formatter.Format(message);
    }

    public object Deserialize(string payload, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!typeof(IMessage).IsAssignableFrom(type))
            throw new ArgumentException("Type must implement IMessage.", nameof(type));

        var parser = MessageParserCache.Get(type);

        return parser.ParseJson(payload);
    }
}