using DomainBlocks.Core.Exceptions;
using DomainBlocks.EventStore.Filtering.Nodes;

namespace DomainBlocks.EventStore.Filtering;

/// <summary>
/// A store was given a filter that it cannot carry out as requested.
/// </summary>
public sealed class EventFilterNotSupportedException(string message) : DomainBlocksException(message)
{
    /// <summary>
    /// Throws if a non-trivial filter was given.
    /// </summary>
    /// <remarks>
    /// Use this when <paramref name="storeName"/> does not support event filters.
    /// </remarks>
    public static void ThrowIfFiltered(EventFilter? filter, string storeName)
    {
        if (filter is not (null or AllEventsFilter))
        {
            throw new EventFilterNotSupportedException(
                $"'{storeName}' does not support event filters, but was given '{filter}'.");
        }
    }
}