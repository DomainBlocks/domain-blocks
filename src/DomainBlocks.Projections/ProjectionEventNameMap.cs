using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Projections;

public sealed class ProjectionEventNameMap : IProjectionEventNameMap
{
    private readonly Dictionary<string, Type> _defaultEventNameMap = new();
    private readonly Dictionary<string, HashSet<Type>> _eventNameMap = new();

    public IEnumerable<Type> GetClrTypesForEventName(string eventName)
    {
        if (eventName == null) throw new ArgumentNullException(nameof(eventName));

        if (_defaultEventNameMap.ContainsKey(eventName))
        {
            return EnumerableEx.Return(_defaultEventNameMap[eventName]);
        }

        return _eventNameMap.TryGetValue(eventName, out var types) ? 
            types : 
            Enumerable.Empty<Type>();
    }

    public void OverrideEventNames<TEvent>(params string[] eventNames)
    {
        if (eventNames == null) throw new ArgumentNullException(nameof(eventNames));

        _defaultEventNameMap.Remove(GetDefaultEventName<TEvent>());

        foreach (var eventName in eventNames)
        {
            if (_eventNameMap.TryGetValue(eventName, out var types))
            {
                types.Add(typeof(TEvent));
            }
            else
            {
                var typesSet = new HashSet<Type> { typeof(TEvent) };
                _eventNameMap.Add(eventName, typesSet);
            }
        }
    }

    public void RegisterDefaultEventName<TEvent>()
    {
        var defaultEventName = GetDefaultEventName<TEvent>();
        if (!_defaultEventNameMap.ContainsKey(defaultEventName))
        {
            _defaultEventNameMap.Add(defaultEventName, typeof(TEvent));
        }
        else
        {
            if (_defaultEventNameMap[defaultEventName] != typeof(TEvent))
            {
                throw new InvalidOperationException($"The name {defaultEventName} has already been registered " +
                                                    $"to type {_defaultEventNameMap[defaultEventName].FullName}. " +
                                                    $"Cannot also register is to type {typeof(TEvent).FullName}");
            }
        }
    }

    private static string GetDefaultEventName<TEvent>()
    {
        return typeof(TEvent).Name;
    }
}