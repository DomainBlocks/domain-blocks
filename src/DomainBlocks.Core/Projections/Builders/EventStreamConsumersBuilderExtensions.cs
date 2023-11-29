using DomainBlocks.Core.Subscriptions.Builders;

namespace DomainBlocks.Core.Projections.Builders;

public static class EventStreamConsumersBuilderExtensions
{
    public static ProjectionsBuilder<TEvent, TPosition> ProjectTo<TEvent, TPosition>(
        this EventStreamConsumersBuilder<TEvent, TPosition> consumersBuilder)
        where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
    {
        return new ProjectionsBuilder<TEvent, TPosition>(consumersBuilder);
    }
}