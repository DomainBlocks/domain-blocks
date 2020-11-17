using DomainLib.Aggregates.Registration;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace DomainLib.Tests.Aggregates
{
    public static class EventNames
    {
        public const string SomethingHappened = "SomethingHappened";
        public const string SomethingElseHappened = "SomethingElseHappened";
    }

    [TestFixture]
    public class AggregateRegistrationTests
    {
        [Test]
        public void EventIsRoutedAndApplied()
        {
            // Setup the aggregate registry.
            var builder = new AggregateRegistryBuilder<object, IEvent>();
            builder.Register<MyAggregateRoot>(agg =>
            {
                agg.Event<SomethingHappened>().RoutesTo((a, e) => a.ApplyEvent(e)).HasName(EventNames.SomethingHappened);
                agg.Event<SomethingElseHappened>().RoutesTo((a, e) => a.ApplyEvent(e)).HasName(EventNames.SomethingElseHappened);
            });

            var registry = builder.Build();
            
            // Set up the initial state.
            var initialState = new MyAggregateRoot();
            
            // Create some events to apply to the aggregate.
            var event1 = new SomethingHappened("Foo");
            var event2 = new SomethingElseHappened("Bar");
            
            // Route the events.
            var newState = registry.EventDispatcher.DispatchEvents(initialState, event1, event2);
            
            Assert.That(newState.State, Is.EqualTo("Foo"));
            Assert.That(newState.OtherState, Is.EqualTo("Bar"));
        }

        [Test]
        public void MissingEventRouteThrowsException()
        {
            // Setup the aggregate registry.
            var builder = new AggregateRegistryBuilder<object, IEvent>();
            builder.Register<MyAggregateRoot>(agg =>
            {
                agg.Event<SomethingHappened>().RoutesTo((a, e) => a.ApplyEvent(e)).HasName(EventNames.SomethingHappened);
            });

            var registry = builder.Build();

            // Set up the initial state.
            var initialState = new MyAggregateRoot();

            // Create some events to apply to the aggregate.
            var event1 = new SomethingHappened("Foo");
            var event2 = new SomethingElseHappened("Bar");

            // Route the events.
            var exception = Assert.Throws<InvalidOperationException>(() => registry.EventDispatcher.DispatchEvents(initialState, event1, event2));
            const string expectedMessage = "No route or default route found when attempting to apply event " +
                                           "SomethingElseHappened to MyAggregateRoot";
            Assert.That(exception.Message, Is.EqualTo(expectedMessage));
        }
        
        [Test]
        public void DefaultEventRouteIsInvokedWhenSpecified()
        {
            // Setup the aggregate registry.
            var builder = new AggregateRegistryBuilder<object, IEvent>();
            builder.Register<MyAggregateRoot>(agg =>
            {
                agg.Event<SomethingHappened>().RoutesTo((a, e) => a.ApplyEvent(e)).HasName(EventNames.SomethingHappened);
                agg.Event<IEvent>().RoutesTo((a, e) => a.DefaultApplyEvent(e));
            });

            var registry = builder.Build();

            // Set up the initial state.
            var initialState = new MyAggregateRoot();

            // Create some events to apply to the aggregate.
            var event1 = new SomethingHappened("Foo");
            var event2 = new SomethingElseHappened("Bar");

            // Route the events.
            var newState = registry.EventDispatcher.DispatchEvents(initialState, event1, event2);
            
            Assert.That(newState.State, Is.EqualTo("Foo"));
            
            // We expect SomethingElseHappened to do nothing, since the default route just returns "this".
            Assert.That(newState.OtherState, Is.Null);
        }

        [Test]
        public void EventNamesAreMappedWhenEventsAreRegisteredWithName()
        {
            // Setup the aggregate registry.
            var builder = new AggregateRegistryBuilder<object, IEvent>();
            builder.Register<MyAggregateRoot>(agg =>
            {
                agg.Event<SomethingHappened>().RoutesTo((a, e) => a.ApplyEvent(e)).HasName(EventNames.SomethingHappened);
                agg.Event<SomethingElseHappened>().RoutesTo((a, e) => a.ApplyEvent(e)).HasName(EventNames.SomethingElseHappened);
            });

            var registry = builder.Build();

            Assert.That(registry.EventNameMap.GetEventNameForClrType(typeof(SomethingHappened)),
                        Is.EqualTo(EventNames.SomethingHappened));

            Assert.That(registry.EventNameMap.GetEventNameForClrType(typeof(SomethingElseHappened)),
                        Is.EqualTo(EventNames.SomethingElseHappened));
        }

        [Test]
        public void CommandIsRoutedAndApplied()
        {
            // Setup the aggregate registry.
            var builder = new AggregateRegistryBuilder<object, IEvent>();
            builder.Register<MyAggregateRoot>(agg =>
            {
                agg.Command<DoSomething>().RoutesTo((a, c) => a().ExecuteCommand(a, c));
                agg.Event<SomethingHappened>().RoutesTo((a, e) => a.ApplyEvent(e)).HasName(EventNames.SomethingHappened);
            });

            var registry = builder.Build();

            // Set up the initial state.
            var initialState = new MyAggregateRoot();

            // Create command to execute on the aggregate.
            var cmd = new DoSomething("Foo");

            // Execute the command
            var result = registry.CommandDispatcher.Dispatch(initialState, cmd);

            Func<SomethingHappened, SomethingHappened, bool> eventComparer = (a, b) => a.State == b.State;

            Assert.That(result.NewState.State, Is.EqualTo("Foo"));
            Assert.That(result.AppliedEvents,
                        Is.EquivalentTo(new object[] {new SomethingHappened("Foo")})
                          .Using(eventComparer));
        }

        [Test]
        public void MissingCommentRegistrationThrowsException()
        {
            // Setup the aggregate registry.
            var builder = new AggregateRegistryBuilder<object, IEvent>();
            builder.Register<MyAggregateRoot>(agg =>
            {
                agg.Command<DoSomething>().RoutesTo((a, c) => a().ExecuteCommand(a, c));
                agg.Event<SomethingHappened>().RoutesTo((a, e) => a.ApplyEvent(e)).HasName(EventNames.SomethingHappened);
            });

            var registry = builder.Build();

            // Set up the initial state.
            var initialState = new MyAggregateRoot();

            // Create command to execute on the aggregate.
            var cmd = new DoSomethingElse("Foo");

            // Route the events.
            var exception = Assert.Throws<InvalidOperationException>(() => registry.CommandDispatcher.Dispatch(initialState, cmd));
            const string expectedMessage = "No route found when attempting to apply command " +
                                           "DoSomethingElse to MyAggregateRoot";
            Assert.That(exception.Message, Is.EqualTo(expectedMessage));
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

        private class DoSomething
        {
            public DoSomething(string state)
            {
                State = state;
            }

            public string State { get; }
        }

        private class DoSomethingElse
        {
            public DoSomethingElse(string state)
            {
                State = state;
            }

            public string State { get; }
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

            public IEnumerable<IEvent> ExecuteCommand(Func<MyAggregateRoot> getState, DoSomething command)
            {
                yield return new SomethingHappened(command.State);
            }

            public IEnumerable<IEvent> ExecuteCommand(Func<MyAggregateRoot> getState, DoSomethingElse command)
            {
                yield return new SomethingHappened(command.State);
            }

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