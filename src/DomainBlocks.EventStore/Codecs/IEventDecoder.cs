using System.Reflection;

namespace DomainBlocks.EventStore.Codecs;

/// <summary>
/// Decodes one persisted record into an event and its metadata.
/// </summary>
public interface IEventDecoder<TEvent, in TEventData, in TMetadata> where TEvent : notnull where TEventData : notnull
{
    /// <param name="eventName">The stored name of the event.</param>
    /// <param name="eventData">The stored event data.</param>
    /// <param name="metadata">
    /// The stored metadata, or <see langword="default"/> when the record has none or the read excluded it.
    /// </param>
    DecodedEvent<TEvent> Decode(string eventName, TEventData eventData, TMetadata? metadata);

    /// <summary>
    /// The names of the stored events that are decoded as <paramref name="eventType"/>, or as a type derived from
    /// it. A filter by event type is turned into one by these names, which a database can evaluate.
    /// </summary>
    IReadOnlyCollection<string> ResolveEventNames(Type eventType);

    /// <summary>
    /// Where in the stored payload of a <paramref name="eventType"/> the value is that <paramref name="members"/> lead
    /// to, going from the event: the names they are stored under, separated by dots. It is <see langword="null"/>
    /// unless that is known of every event that is read as the type, which is the answer of a decoder that does not
    /// say otherwise.
    /// </summary>
    string? ResolveStoredPath(Type eventType, IReadOnlyList<MemberInfo> members) => null;
}