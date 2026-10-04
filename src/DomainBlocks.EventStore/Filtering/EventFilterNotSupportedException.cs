using DomainBlocks.Core.Exceptions;
using DomainBlocks.EventStore.Filtering.Nodes;

namespace DomainBlocks.EventStore.Filtering;

/// <summary>
/// Thrown when a store is given an event filter that it cannot apply.
/// </summary>
public sealed class EventFilterNotSupportedException(string message) : DomainBlocksException(message)
{
    /// <summary>
    /// Throws if <paramref name="filter"/> is anything other than <see cref="EventFilter.All"/>. An operation that does
    /// not filter calls this so that a filter is refused rather than ignored.
    /// </summary>
    /// <param name="filter">The filter that was given, or <see langword="null"/> if none was.</param>
    /// <param name="operation">The name of the operation that does not filter, for the message.</param>
    public static void ThrowIfFiltered(EventFilter? filter, string operation)
    {
        if (filter is null or AllEventsFilter)
            return;

        throw new EventFilterNotSupportedException(
            $"{operation} does not support event filters, but was given the filter {filter}.");
    }
}