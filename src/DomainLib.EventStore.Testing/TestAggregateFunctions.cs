﻿using System;
using System.Collections.Generic;
using DomainLib.Aggregates.Registration;

namespace DomainLib.EventStore.Testing
{
    public class TestAggregateFunctions
    {
        public static void Register(AggregateRegistryBuilder<TestCommand, TestEvent> builder)
        {
            builder.Register<TestAggregateState>(agg =>
            {
                agg.Id(x => x.Id.ToString())
                   .PersistenceKey(id => $"testAggregate-{id}")
                   .SnapshotKey(id => $"testAggregateSnapshot-{id}");

                agg.Command<TestCommand>().RoutesTo(Execute);
                agg.Event<TestEvent>().HasName(nameof(TestEvent)).RoutesTo(Apply);
            });
        }

        public static IEnumerable<TestEvent> Execute(Func<TestAggregateState> getState, TestCommand command)
        {
            yield return new TestEvent(command.Number);
        }

        public static TestAggregateState Apply(TestAggregateState initialState, TestEvent @event)
        {
            return new TestAggregateState(initialState.Id, initialState.TotalNumber + @event.Number);
        }
    }
}