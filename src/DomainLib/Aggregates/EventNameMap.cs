using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace DomainLib.Aggregates
{
    // ReSharper disable once InconsistentNaming
    public class EventNameMap : IEventNameMap
    {
        private readonly Dictionary<string, Type> _eventNameToTypeMap;
        private readonly Dictionary<Type, string> _eventTypeToNameMap;

        public EventNameMap()
        {
            _eventNameToTypeMap = new Dictionary<string, Type>();
            _eventTypeToNameMap = new Dictionary<Type, string>();
        }
        
        public void RegisterEvent<TEvent>(bool throwOnConflict = true)
        {
            RegisterEvent(typeof(TEvent), throwOnConflict);
        }

        public void RegisterEvent(Type clrType, bool throwOnConflict = true)
        {
            var eventName = DetermineEventNameForClrType(clrType);
            RegisterEventName(eventName, clrType, throwOnConflict);
        }

        public Type GetClrTypeForEventName(string eventName)
        {
            if (_eventNameToTypeMap.TryGetValue(eventName, out var clrType))
            {
                return clrType;
            }

            throw new UnknownEventNameException($"{eventName} could not be mapped to a CLR type", eventName);
        }

        public string GetEventNameForClrType(Type clrType)
        {
            return _eventTypeToNameMap.TryGetValue(clrType, out var eventName)
                ? eventName
                : DetermineEventNameForClrType(clrType);
        }

        IEnumerable<KeyValuePair<string, Type>> IEventNameMap.GetNameToTypeMappings()
        {
            return _eventNameToTypeMap.ToImmutableDictionary();
        }

        public void Merge(IEventNameMap other, bool throwOnConflict = true)
        {
            foreach (var (key, value) in other.GetNameToTypeMappings())
            {
                RegisterEventName(key, value, throwOnConflict);
            }
        }

        private static string DetermineEventNameForClrType(Type clrType)
        {
            // When determining the event name for a given type, we follow a three step process.
            // 1) If there is an EventNameAttribute on the type, we use the event name from that.
            // 2) If there is a public static field with the name 'EventName' on the type, use that
            // 3) Use the name of the type

            var eventNameAttribute = clrType
                                     .GetCustomAttributes(typeof(EventNameAttribute), true)
                                     .Cast<EventNameAttribute>()
                                     .ToList();

            // We shouldn't be able to get here as the attribute is restricted to prevent 
            // multiple uses. Defensive check to avoid any ambiguity 
            if (eventNameAttribute.Count > 1)
            {
                throw new InvalidOperationException($"Found more than one EventNameAttribute for type {clrType.FullName}");
            }

            if (eventNameAttribute.Count == 1)
            {
                return eventNameAttribute[0].EventName;
            }
            
            var eventName = clrType.GetField("EventName")?.GetValue(null) as string;
            
            return !string.IsNullOrEmpty(eventName) ? eventName : clrType.Name;
        }

        private void RegisterEventName(string eventName, Type clrType, bool throwOnConflict = true)
        {
            if (throwOnConflict && 
                _eventNameToTypeMap.ContainsKey(eventName) &&
                _eventNameToTypeMap[eventName] != clrType)
            {
                throw new InvalidOperationException($"Event {eventName} is already mapped " +
                                                    $"to type {_eventNameToTypeMap[eventName].FullName}. " +
                                                    $"Cannot map to {clrType.FullName}");
            }

            if (throwOnConflict && 
                _eventTypeToNameMap.ContainsKey(clrType) &&
                _eventTypeToNameMap[clrType] != eventName)
            {
                throw new InvalidOperationException($"Type {clrType.FullName} is already mapped " +
                                                    $"to event {_eventTypeToNameMap[clrType]}. " +
                                                    $"Cannot map to {_eventTypeToNameMap[clrType]}");
            }

            _eventNameToTypeMap[eventName] = clrType;
            _eventTypeToNameMap[clrType] = eventName;
        }
    }
}