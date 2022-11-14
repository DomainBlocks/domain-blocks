using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DomainBlocks.Projections;

public sealed class ProjectionEventNameMap : IProjectionEventNameMap
{
    private readonly Dictionary<string, Type> _defaultEventNameMap = new();
    private readonly Dictionary<string, HashSet<Type>> _eventNameMap = new();

    public ProjectionEventNameMap()
    {
    }

    public ProjectionEventNameMap(ProjectionEventNameMap copyFrom)
    {
        _defaultEventNameMap = new Dictionary<string, Type>(copyFrom._defaultEventNameMap);
        _eventNameMap = copyFrom._eventNameMap.ToDictionary(x => x.Key, x => new HashSet<Type>(x.Value));
    }

    public IEnumerable<Type> GetClrTypesForEventName(string eventName)
    {
        if (eventName == null) throw new ArgumentNullException(nameof(eventName));

        if (_defaultEventNameMap.ContainsKey(eventName))
        {
            return EnumerableEx.Return(_defaultEventNameMap[eventName]);
        }

        return _eventNameMap.TryGetValue(eventName, out var types) ? types : Enumerable.Empty<Type>();
    }

    public ProjectionEventNameMap OverrideEventNames<TEvent>(params string[] eventNames)
    {
        if (eventNames == null) throw new ArgumentNullException(nameof(eventNames));

        var copy = new ProjectionEventNameMap(this);
        copy._defaultEventNameMap.Remove(GetDefaultEventName<TEvent>());

        foreach (var eventName in eventNames)
        {
            if (copy._eventNameMap.TryGetValue(eventName, out var types))
            {
                types.Add(typeof(TEvent));
            }
            else
            {
                copy._eventNameMap.Add(eventName, new HashSet<Type> { typeof(TEvent) });
            }
        }

        return copy;
    }

    public ProjectionEventNameMap RegisterDefaultEventName<TEvent>()
    {
        return RegisterDefaultEventName(typeof(TEvent));
    }

    public ProjectionEventNameMap RegisterDefaultEventName(Type eventType)
    {
        var defaultEventName = GetDefaultEventName(eventType);

        if (_defaultEventNameMap.TryGetValue(defaultEventName, out var mappedEventType))
        {
            if (eventType != mappedEventType)
            {
                throw new InvalidOperationException(
                    $"The name {defaultEventName} has already been registered for type {mappedEventType.FullName}. " +
                    $"Cannot also register it for type {eventType.FullName}");
            }

            return this;
        }

        var copy = new ProjectionEventNameMap(this);
        copy._defaultEventNameMap.Add(defaultEventName, eventType);
        return copy;
    }

    private static string GetDefaultEventName<TEvent>()
    {
        return GetDefaultEventName(typeof(TEvent));
    }

    private static string GetDefaultEventName(MemberInfo eventType)
    {
        return eventType.Name;
    }
}