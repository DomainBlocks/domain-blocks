using System.Collections.Generic;
using DomainBlocks.Aggregates;
using DomainBlocks.Aggregates.Builders;
using NUnit.Framework;

namespace DomainBlocks.Tests.Aggregates;

[TestFixture]
public class EventDispatcherTests
{
    [Test]
    public void EventIsRoutedAndApplied()
    {
        var registry = EventRegistryBuilder
            .OfType<object>()
            .For<MyAggregateRoot>(events =>
            {
                events.Event<SomethingHappened>()
                    .RoutesTo((a, e) => a.ApplyEvent(e))
                    .HasName(EventNames.SomethingHappened);

                events.Event<SomethingElseHappened>()
                    .RoutesTo((a, e) => a.ApplyEvent(e))
                    .HasName(EventNames.SomethingElseHappened);
            })
            .Build();

        var eventDispatcher = new EventDispatcher<object>(registry.EventRoutes);

        // Set up the initial state.
        var initialState = new MyAggregateRoot();

        // Create some events to apply to the aggregate.
        var event1 = new SomethingHappened("Foo");
        var event2 = new SomethingElseHappened("Bar");

        // Route the events.
        var newState = eventDispatcher.Dispatch(initialState, event1, event2);

        Assert.That(newState.State, Is.EqualTo("Foo"));
        Assert.That(newState.OtherState, Is.EqualTo("Bar"));
    }

    [Test]
    public void MissingEventRouteThrowsException()
    {
        var registry = EventRegistryBuilder
            .OfType<object>()
            .For<MyAggregateRoot>(events =>
            {
                events.Event<SomethingHappened>()
                    .RoutesTo((a, e) => a.ApplyEvent(e))
                    .HasName(EventNames.SomethingElseHappened);
            })
            .Build();

        var eventDispatcher = new EventDispatcher<object>(registry.EventRoutes);

        // Set up the initial state.
        var initialState = new MyAggregateRoot();

        // Create some events to apply to the aggregate.
        var event1 = new SomethingHappened("Foo");
        var event2 = new SomethingElseHappened("Bar");

        // Route the events.
        var exception =
            Assert.Throws<KeyNotFoundException>(() => eventDispatcher.Dispatch(initialState, event1, event2));

        const string expectedMessage = "No route or default route found when attempting to apply event " +
                                       "SomethingElseHappened to MyAggregateRoot";

        Assert.That(exception.Message, Is.EqualTo(expectedMessage));
    }

    [Test]
    public void DefaultEventRouteIsInvokedWhenSpecified()
    {
        var registry = EventRegistryBuilder.OfType<IEvent>()
            .For<MyAggregateRoot>(events =>
            {
                events.Event<SomethingHappened>()
                    .RoutesTo((a, e) => a.ApplyEvent(e))
                    .HasName(EventNames.SomethingHappened);

                events.Event<IEvent>()
                    .RoutesTo((a, e) => a.DefaultApplyEvent(e));
            })
            .Build();

        var eventDispatcher = new EventDispatcher<IEvent>(registry.EventRoutes);

        // Set up the initial state.
        var initialState = new MyAggregateRoot();

        // Create some events to apply to the aggregate.
        var event1 = new SomethingHappened("Foo");
        var event2 = new SomethingElseHappened("Bar");

        // Route the events.
        var newState = eventDispatcher.Dispatch(initialState, event1, event2);

        Assert.That(newState.State, Is.EqualTo("Foo"));

        // We expect SomethingElseHappened to do nothing, since the default route just returns "this".
        Assert.That(newState.OtherState, Is.Null);
    }

    private interface IEvent
    {
    }

    private class SomethingHappened : IEvent
    {
        public SomethingHappened(string state)
        {
            State = state;
        }

        public string State { get; }
    }

    private class SomethingElseHappened : IEvent
    {
        public SomethingElseHappened(string otherState)
        {
            OtherState = otherState;
        }

        public string OtherState { get; }
    }

    private class MyAggregateRoot
    {
        public MyAggregateRoot()
        {
        }

        private MyAggregateRoot(string state, string otherState)
        {
            State = state;
            OtherState = otherState;
        }

        public string State { get; }
        public string OtherState { get; }

        public MyAggregateRoot ApplyEvent(SomethingHappened @event)
        {
            return new MyAggregateRoot(@event.State, OtherState);
        }

        public MyAggregateRoot ApplyEvent(SomethingElseHappened @event)
        {
            return new MyAggregateRoot(State, @event.OtherState);
        }

        public MyAggregateRoot DefaultApplyEvent(IEvent @event)
        {
            return this;
        }
    }
}