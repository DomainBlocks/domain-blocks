namespace DomainBlocks.Experimental.EventSourcing.Persistence.Extensions;

public static class SingleEventTypeConfiguratorExtensions
{
    public static SingleEventTypeConfigurator<TEvent, TState> WithName<TEvent, TState>(
        this SingleEventTypeConfigurator<TEvent, TState> configurator, string eventName)
    {
        configurator.GetOrAddExtension().EventName = eventName;
        return configurator;
    }

    public static SingleEventTypeConfigurator<TEvent, TState> WithDeprecatedNames<TEvent, TState>(
        this SingleEventTypeConfigurator<TEvent, TState> configurator, params string[] deprecatedEventNames)
    {
        configurator.GetOrAddExtension().DeprecatedEventNames = deprecatedEventNames;
        return configurator;
    }

    public static SingleEventTypeConfigurator<TEvent, TState> WithMetadata<TEvent, TState>(
        this SingleEventTypeConfigurator<TEvent, TState> configurator,
        Func<IReadOnlyDictionary<string, string>> metadataFactory)
    {
        configurator.GetOrAddExtension().MetadataFactory = metadataFactory;
        return configurator;
    }

    public static SingleEventTypeConfigurator<TEvent, TState> WithReadCondition<TEvent, TState>(
        this SingleEventTypeConfigurator<TEvent, TState> configurator,
        EventReadCondition readCondition)
    {
        configurator.GetOrAddExtension().ReadCondition = readCondition;
        return configurator;
    }

    public static SingleEventTypeConfigurator<TEvent, TState> WithUpcastTo<TEvent, TState, TUpcastEvent>(
        this SingleEventTypeConfigurator<TEvent, TState> configurator,
        Func<TEvent, TUpcastEvent> upcastFunc)
    {
        configurator.GetOrAddExtension().EventUpcaster = EventUpcaster.Create(upcastFunc);
        return configurator;
    }

    private static SingleEventTypeConfigExtension GetOrAddExtension(this IConfigExtensionsSource source)
    {
        return source.GetOrAddExtension<SingleEventTypeConfigExtension>();
    }

    private static TExtension GetOrAddExtension<TExtension>(this IConfigExtensionsSource source)
        where TExtension : IConfigExtension, new()
    {
        var extension = source.ConfigExtensions.SingleOrDefault(x => x is TExtension);

        if (extension == null)
        {
            extension = new TExtension();
            source.ConfigExtensions.Add(extension);
        }

        return (TExtension)extension;
    }
}