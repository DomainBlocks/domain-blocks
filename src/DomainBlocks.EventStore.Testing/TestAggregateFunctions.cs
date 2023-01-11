using DomainBlocks.Core.Builders;

namespace DomainBlocks.EventStore.Testing;

public static class TestAggregateFunctions
{
    public static void BuildModel(ModelBuilder builder, Guid aggregateId)
    {
        builder.ImmutableAggregate<TestAggregateState, object>(aggregate =>
        {
            aggregate
                .InitialState(() => new TestAggregateState(aggregateId, 0))
                .HasId(x => x.Id.ToString())
                .WithStreamKey(id => $"testAggregate-{id}")
                .WithSnapshotKey(id => $"testAggregateSnapshot-{id}");

            aggregate
                .CommandResult<IEnumerable<object>>()
                .WithEventsFrom(x => x);

            aggregate.ApplyEventsWith((agg, e) => Apply(agg, (dynamic)e));

            aggregate.Event<TestEvent>();
        });
    }

    public static IEnumerable<object> Execute(TestCommand command)
    {
        yield return new TestEvent(command.Number);
    }

    private static TestAggregateState Apply(TestAggregateState initialState, TestEvent @event)
    {
        return new TestAggregateState(initialState.Id, initialState.TotalNumber + @event.Number);
    }
}