using DomainBlocks.Core.Exceptions;
using DomainBlocks.EventStore.Filtering.Nodes;

namespace DomainBlocks.EventStore.Filtering;

/// <summary>
/// The exception that is thrown when a store is given an event filter it cannot apply.
/// </summary>
public sealed class EventFilterNotSupportedException(string message) : DomainBlocksException(message)
{
    /// <summary>
    /// Throws if <paramref name="filter"/> is anything but <see cref="EventFilter.All"/>, so an operation that cannot
    /// filter refuses a filter rather than ignoring it.
    /// </summary>
    /// <param name="filter">The filter, or <see langword="null"/> if none was given.</param>
    /// <param name="operation">The name of the operation, for the exception message.</param>
    public static void ThrowIfFiltered(EventFilter? filter, string operation)
    {
        if (filter is null or AllEventsFilter)
            return;

        throw new EventFilterNotSupportedException(
            $"{operation} does not support event filters, but was given the filter {filter}.");
    }
}