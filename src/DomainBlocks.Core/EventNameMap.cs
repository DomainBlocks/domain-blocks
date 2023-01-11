namespace DomainBlocks.Core;

public sealed class EventNameMap : IEventNameMap
{
    private readonly Dictionary<string, Type> _eventNameToTypeMap;
    private readonly Dictionary<Type, string> _eventTypeToNameMap;

    public EventNameMap()
    {
        _eventNameToTypeMap = new Dictionary<string, Type>();
        _eventTypeToNameMap = new Dictionary<Type, string>();
    }

    public void Add(string eventName, Type eventType, bool throwOnConflict = true)
    {
        if (string.IsNullOrEmpty(eventName))
            throw new ArgumentException("Event name cannot be null or empty", nameof(eventName));
        
        if (eventType == null) throw new ArgumentNullException(nameof(eventType));
        
        switch (throwOnConflict)
        {
            case true when _eventNameToTypeMap.ContainsKey(eventName) && _eventNameToTypeMap[eventName] != eventType:
                throw new InvalidOperationException(
                    $"Event name '{eventName}' is already mapped to CLR type " +
                    $"'{_eventNameToTypeMap[eventName].FullName}'. Cannot map to '{eventType.FullName}'");
            case true when _eventTypeToNameMap.ContainsKey(eventType) && _eventTypeToNameMap[eventType] != eventName:
                throw new InvalidOperationException(
                    $"CLR type '{eventType.FullName}' is already mapped to event name " +
                    $"'{_eventTypeToNameMap[eventType]}'. Cannot map to '{eventName}'");
            default:
                _eventNameToTypeMap[eventName] = eventType;
                _eventTypeToNameMap[eventType] = eventName; 
                break;
        }
    }

    public Type GetEventType(string eventName)
    {
        return _eventNameToTypeMap.TryGetValue(eventName, out var clrType)
            ? clrType
            : throw new UnmappedEventNameException(eventName);
    }

    public string GetEventName(Type eventType)
    {
        return _eventTypeToNameMap.TryGetValue(eventType, out var eventName)
            ? eventName
            : throw new UnmappedEventTypeException(eventType.FullName!);
    }
}