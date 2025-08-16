using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Google.Protobuf;

namespace DomainBlocks.Serialization.GoogleProtobuf;

internal static class MessageParserCache
{
    private static readonly ConcurrentDictionary<Type, Func<MessageParser>> ParserFactories = new();

    public static MessageParser GetParser(Type messageType)
    {
        var factory = ParserFactories.GetOrAdd(messageType, CreateParserFactory);
        return factory();
    }

    private static Func<MessageParser> CreateParserFactory(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;

        var parserProperty = type.GetProperty("Parser", flags);
        if (parserProperty == null || !typeof(MessageParser).IsAssignableFrom(parserProperty.PropertyType))
        {
            throw new ArgumentException(
                $"Type '{type.FullName}' does not expose a valid static Parser property.",
                nameof(type));
        }

        // Expression: () => (MessageParser)Type.Parser
        var propertyExpr = Expression.Property(null, parserProperty);
        var convertExpr = Expression.Convert(propertyExpr, typeof(MessageParser));
        var lambdaExpr = Expression.Lambda<Func<MessageParser>>(convertExpr);

        return lambdaExpr.Compile();
    }
}