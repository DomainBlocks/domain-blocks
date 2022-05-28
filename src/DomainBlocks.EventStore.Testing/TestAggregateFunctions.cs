using System;
using System.Collections.Generic;
using DomainBlocks.Persistence.Builders;

namespace DomainBlocks.EventStore.Testing;

public static class TestAggregateFunctions
{
    public static void Register(AggregateRegistryBuilder<TestEvent> builder, Guid aggregateId)
    {
        builder.Register<TestAggregateState>(agg =>
        {
            agg.InitialState(_ => new TestAggregateState(aggregateId, 0))
                .Id(x => x.Id.ToString())
                .PersistenceKey(id => $"testAggregate-{id}")
                .SnapshotKey(id => $"testAggregateSnapshot-{id}");

            agg.RegisterEvents(x => { x.Event<TestEvent>().RoutesTo(Apply); });
        });
    }

    public static IEnumerable<TestEvent> Execute(TestCommand command)
    {
        yield return new TestEvent(command.Number);
    }

    private static TestAggregateState Apply(TestAggregateState initialState, TestEvent @event)
    {
        return new TestAggregateState(initialState.Id, initialState.TotalNumber + @event.Number);
    }
}