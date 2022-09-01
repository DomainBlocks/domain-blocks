﻿using System;
using System.Collections.Generic;
using System.Linq;
using DomainBlocks.Persistence.New.Builders;

namespace DomainBlocks.EventStore.Testing;

public static class TestAggregateFunctions
{
    public static void BuildModel(ModelBuilder builder, Guid aggregateId)
    {
        builder
            .Aggregate<TestAggregateState, object>(aggregate =>
            {
                aggregate
                    .InitialState(() => new TestAggregateState(aggregateId, 0))
                    .HasId(x => x.Id.ToString())
                    .WithStreamKey(id => $"testAggregate-{id}")
                    .WithSnapshotKey(id => $"testAggregateSnapshot-{id}");

                aggregate
                    .CommandResult<IEnumerable<object>>()
                    .WithEventsFrom((res, _) => res)
                    .WithUpdatedStateFrom((res, agg) => res.Aggregate(agg, (acc, next) => Apply(acc, (dynamic)next)));

                aggregate.ApplyEventsWith((agg, e) => Apply(agg, (dynamic)e));

                aggregate.Event<TestEvent>().HasName(nameof(TestEvent));
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