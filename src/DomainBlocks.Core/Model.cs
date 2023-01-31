namespace DomainBlocks.Core;

public sealed class Model
{
    private readonly IReadOnlyDictionary<Type, IAggregateOptions> _aggregatesOptions;
    private readonly EventNameMap _eventNameMap;

    public Model(IEnumerable<IAggregateOptions> aggregatesOptions)
    {
        if (aggregatesOptions == null) throw new ArgumentNullException(nameof(aggregatesOptions));

        _aggregatesOptions = aggregatesOptions.ToDictionary(x => x.ClrType);

        // Build event name map from all aggregate options.
        var eventTypes = _aggregatesOptions.Values.SelectMany(x => x.EventTypes);
        _eventNameMap = new EventNameMap();
        foreach (var eventType in eventTypes)
        {
            _eventNameMap.Add(eventType.EventName, eventType.ClrType);
        }
    }

    public IEventNameMap EventNameMap => _eventNameMap;

    public IAggregateOptions<TAggregate> GetAggregateOptions<TAggregate>()
    {
        return (IAggregateOptions<TAggregate>)_aggregatesOptions[typeof(TAggregate)];
    }
}