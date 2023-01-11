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
        var allEventsOptions = _aggregatesOptions.Values.SelectMany(x => x.EventsOptions);
        _eventNameMap = new EventNameMap();
        foreach (var eventOptions in allEventsOptions)
        {
            _eventNameMap.Add(eventOptions.EventName, eventOptions.ClrType);
        }
    }

    public IEventNameMap EventNameMap => _eventNameMap;

    public IAggregateOptions<TAggregate> GetAggregateOptions<TAggregate>()
    {
        return (IAggregateOptions<TAggregate>)_aggregatesOptions[typeof(TAggregate)];
    }
}