namespace DomainBlocks.Experimental.EventSourcing.Persistence;

internal class SingleEventTypeConfigExtension : IConfigExtension
{
    public string? EventName { get; set; }
    public IEnumerable<string>? DeprecatedEventNames { get; set; }
    public Func<IReadOnlyDictionary<string, string>>? MetadataFactory { get; set; }
    public EventReadCondition? ReadCondition { get; set; }
    public EventUpcaster? EventUpcaster { get; set; }

    public IConfigExtension Clone()
    {
        return new SingleEventTypeConfigExtension
        {
            EventName = EventName,
            DeprecatedEventNames = DeprecatedEventNames?.ToList(),
            MetadataFactory = MetadataFactory,
            ReadCondition = ReadCondition,
            EventUpcaster = EventUpcaster
        };
    }
}