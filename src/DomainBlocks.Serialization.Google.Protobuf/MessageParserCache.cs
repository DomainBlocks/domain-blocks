using System.Collections.Concurrent;
using System.Reflection;
using Google.Protobuf;

namespace DomainBlocks.Serialization.Google.Protobuf;

internal static class MessageParserCache
{
    private static readonly ConcurrentDictionary<Type, MessageParser> Parsers = new();

    public static MessageParser Get(Type type)
    {
        return Parsers.GetOrAdd(type, static type =>
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
        });
    }
}