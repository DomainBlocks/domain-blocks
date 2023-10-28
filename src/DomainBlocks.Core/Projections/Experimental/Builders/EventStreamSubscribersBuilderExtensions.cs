using DomainBlocks.Core.Subscriptions.Builders;

namespace DomainBlocks.Core.Projections.Experimental.Builders;

public static class EventStreamSubscribersBuilderExtensions
{
    public static ProjectionsBuilder<TEvent, TPosition> ProjectTo<TEvent, TPosition>(
        this EventStreamConsumersBuilder<TEvent, TPosition> consumersBuilder)
        where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
    {
        return new ProjectionsBuilder<TEvent, TPosition>(consumersBuilder);
    }
}