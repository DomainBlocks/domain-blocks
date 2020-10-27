using System;
using DomainLib.Aggregates;
using NUnit.Framework;

namespace DomainLib.Tests.Aggregates
{
    public static class EventNames
    {
        public const string SomethingHappened = "SomethingHappened";
        public const string SomethingElseHappened = "SomethingElseHappened";
    }

    [TestFixture]
    public class ApplyEventRouterTests
    {
        [Test]
        public void EventIsRoutedAndApplied()
        {
            // Setup the event router.
            var builder = new ApplyEventRouterBuilder<MyAggregateRoot, IEvent>();
            builder.Add<SomethingHappened>((agg, e) => agg.ApplyEvent(e));
            builder.Add<SomethingElseHappened>((agg, e) => agg.ApplyEvent(e));
            var router = builder.Build();
            
            // Set up the initial state.
            var initialState = new MyAggregateRoot();
            
            // Create some events to apply to the aggregate.
            var event1 = new SomethingHappened("Foo");
            var event2 = new SomethingElseHappened("Bar");
            
            // Route the events.
            var newState = router.Route(initialState, event1, event2);
            
            Assert.That(newState.State, Is.EqualTo("Foo"));
            Assert.That(newState.OtherState, Is.EqualTo("Bar"));
        }

        [Test]
        public void MissingEventRouteThrowsException()
        {
            // Setup the event router.
            var builder = new ApplyEventRouterBuilder<MyAggregateRoot, IEvent>();
            builder.Add<SomethingHappened>((agg, e) => agg.ApplyEvent(e));
            var router = builder.Build();

            // Set up the initial state.
            var initialState = new MyAggregateRoot();

            // Create some events to apply to the aggregate.
            var event1 = new SomethingHappened("Foo");
            var event2 = new SomethingElseHappened("Bar");

            // Route the events.
            var exception = Assert.Throws<InvalidOperationException>(() => router.Route(initialState, event1, event2));
            const string expectedMessage = "No route or default route found when attempting to apply " +
                                           "SomethingElseHappened to MyAggregateRoot";
            Assert.That(exception.Message, Is.EqualTo(expectedMessage));
        }
        
        [Test]
        public void DefaultEventRouteIsInvokedWhenSpecified()
        {
            // Setup the event router.
            var builder = new ApplyEventRouterBuilder<MyAggregateRoot, IEvent>();
            builder.Add<SomethingHappened>((agg, e) => agg.ApplyEvent(e));
            builder.Add<IEvent>((agg, e) => agg.DefaultApplyEvent(e)); // Default route
            var router = builder.Build();

            // Set up the initial state.
            var initialState = new MyAggregateRoot();

            // Create some events to apply to the aggregate.
            var event1 = new SomethingHappened("Foo");
            var event2 = new SomethingElseHappened("Bar");

            // Route the events.
            var newState = router.Route(initialState, event1, event2);
            
            Assert.That(newState.State, Is.EqualTo("Foo"));
            
            // We expect SomethingElseHappened to do nothing, since the default route just returns "this".
            Assert.That(newState.OtherState, Is.Null);
        }

        [Test]
        public void EventNamesAreMappedWhenEventRoutesAreAdded()
        {
            var builder = new ApplyEventRouterBuilder<MyAggregateRoot, IEvent>();
            builder.Add<SomethingHappened>((agg, e) => agg.ApplyEvent(e));
            builder.Add<SomethingElseHappened>((agg, e) => agg.ApplyEvent(e));

            var router = builder.Build();

            Assert.That(router.EventNameMap.GetEventNameForClrType(typeof(SomethingHappened)),
                Is.EqualTo(EventNames.SomethingHappened));

            Assert.That(router.EventNameMap.GetEventNameForClrType(typeof(SomethingElseHappened)),
                        Is.EqualTo(EventNames.SomethingElseHappened));
        }

        private interface IEvent
        {
        }

        [EventName(EventNames.SomethingHappened)]
        private class SomethingHappened : IEvent
        {
            public SomethingHappened(string state)
            {
                State = state;
            }

            public string State { get; }
        }

        [EventName(EventNames.SomethingElseHappened)]
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
}