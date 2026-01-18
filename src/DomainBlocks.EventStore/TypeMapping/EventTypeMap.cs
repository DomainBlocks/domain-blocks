namespace DomainBlocks.EventStore.TypeMapping;

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
public class EventTypeMap
{
    internal EventTypeMap(AppendEventTypeMappingSet appendMappings, ReadEventTypeMappingSet readMappings)
    {
        Append = new AppendEventTypeMap(appendMappings);
        Read = new ReadEventTypeMap(readMappings);
    }

    public AppendEventTypeMap Append { get; }
    public ReadEventTypeMap Read { get; }
}