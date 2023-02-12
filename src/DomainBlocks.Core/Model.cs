namespace DomainBlocks.Core;

public sealed class Model
{
    private readonly IReadOnlyDictionary<Type, IAggregateType> _aggregateTypes;
    private readonly EventNameMap _eventNameMap;

    public Model(IEnumerable<IAggregateType> aggregatesTypes)
    {
        if (aggregatesTypes == null) throw new ArgumentNullException(nameof(aggregatesTypes));

        _aggregateTypes = aggregatesTypes.ToDictionary(x => x.ClrType);

        // Build event name map from all aggregate types.
        var eventTypes = _aggregateTypes.Values.SelectMany(x => x.EventTypes);
        _eventNameMap = new EventNameMap();
        foreach (var eventType in eventTypes)
        {
            _eventNameMap.Add(eventType.EventName, eventType.ClrType);
        }
    }

    public IEventNameMap EventNameMap => _eventNameMap;

    public IAggregateType<TAggregate> GetAggregateType<TAggregate>()
    {
        return (IAggregateType<TAggregate>)_aggregateTypes[typeof(TAggregate)];
    }
}