using System.Collections.Frozen;
using DomainBlocks.EventStore.Exceptions;

namespace DomainBlocks.EventStore;

/// <summary>
/// Defines the mapping between event CLR types and their string names used in storage.
/// </summary>
/// <remarks>
/// <para>
/// Type-to-name mappings are used when writing events, and must be one-to-one.
/// </para>
/// <para>
/// Name-to-type mappings are used when reading events, and may be many-to-one to support renamed events, or to
/// deserialize events with a shared structure into a common CLR type.
/// </para>
/// </remarks>
public sealed class EventTypeMap
{
    private readonly FrozenDictionary<Type, string> _writeMap;
    private readonly FrozenDictionary<string, Type> _readMap;

    internal EventTypeMap(EventTypeToNameMappingSet writeMappings, EventNameToTypeMappingSet readMappings)
    {
        _writeMap = writeMappings.ToFrozenDictionary();
        _readMap = readMappings.ToFrozenDictionary();
    }

    /// <summary>
    /// Gets the event name associated with an event type.
    /// </summary>
    public string GetEventName(Type eventType) => _writeMap.GetValueOrDefault(eventType) ??
                                                  throw new EventTypeToNameMappingNotFoundException(eventType);

    /// <summary>
    /// Gets the event type associated with an event name.
    /// </summary>
    public Type GetEventType(string eventName) => _readMap.GetValueOrDefault(eventName) ??
                                                  throw new EventNameToTypeMappingNotFoundException(eventName);
}