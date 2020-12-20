using System;
using System.Collections.Generic;

namespace DomainLib.Aggregates
{
    public sealed class EventNameMap : IEventNameMap
    {
        private readonly Dictionary<string, Type> _eventNameToTypeMap;
        private readonly Dictionary<Type, string> _eventTypeToNameMap;

        public EventNameMap()
        {
            _eventNameToTypeMap = new Dictionary<string, Type>();
            _eventTypeToNameMap = new Dictionary<Type, string>();
        }
        
        public void RegisterEvent<TEvent>(string eventName, bool throwOnConflict = true)
        {
            RegisterEvent(typeof(TEvent), eventName, throwOnConflict);
        }

        public void RegisterEvent(Type clrType, string eventName, bool throwOnConflict = true)
        {
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
                : throw new UnknownEventNameException($"Could not find event name for type {clrType.FullName}");
        }

        // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
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