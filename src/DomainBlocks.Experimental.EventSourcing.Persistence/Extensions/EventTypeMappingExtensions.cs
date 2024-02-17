namespace DomainBlocks.Experimental.EventSourcing.Persistence.Extensions;

public static class EventTypeMappingExtensions
{
    public static ExtendedEventTypeMapping<TState> Extend<TState>(this EventTypeMapping<TState> mapping)
    {
        var extension = mapping.ConfigExtensions
            // Prepend a default value to handle the case where no extension has been added. This is required for the
            // Aggregate linq method, which requires a non-empty sequence.
            .Prepend(new SingleEventTypeConfigExtension())
            .Where(x => x is SingleEventTypeConfigExtension)
            .Cast<SingleEventTypeConfigExtension>()
            .Aggregate((acc, next) =>
            {
                acc.EventName = next.EventName ?? acc.EventName;
                acc.DeprecatedEventNames = next.DeprecatedEventNames ?? acc.DeprecatedEventNames;
                acc.MetadataFactory = next.MetadataFactory ?? acc.MetadataFactory;
                acc.ReadCondition = next.ReadCondition ?? acc.ReadCondition;
                acc.EventUpcaster = next.EventUpcaster ?? acc.EventUpcaster;
                return acc;
            });

        return new ExtendedEventTypeMapping<TState>(
            mapping.EventType,
            mapping.EventApplier,
            extension.EventName,
            extension.DeprecatedEventNames,
            extension.MetadataFactory,
            extension.ReadCondition,
            extension.EventUpcaster);
    }
}