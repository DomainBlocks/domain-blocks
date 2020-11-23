using System;
using System.Collections.Generic;
using DomainLib.Aggregates.Registration;
using DomainLib.Tests.Aggregates.Registration;
using NUnit.Framework;

namespace DomainLib.Tests.Aggregates
{
    [TestFixture]
    public class CommandDispatcherTests
    {
        [Test]
        public void CommandIsRoutedAndApplied()
        {
            // Setup the aggregate registry.
            var builder = new AggregateRegistryBuilder<object, IEvent>();
            builder.Register<MyAggregateRoot>(agg =>
            {
                agg.Command<DoSomething>().RoutesTo((a, c) => a().ExecuteCommand(a, c));
                agg.Event<SomethingHappened>().RoutesTo((a, e) => a.ApplyEvent(e))
                    .HasName(EventNames.SomethingHappened);
            });

            var registry = builder.Build();

            // Set up the initial state.
            var initialState = new MyAggregateRoot();

            // Create command to execute on the aggregate.
            var cmd = new DoSomething("Foo");

            // Execute the command
            var (newState, events) = registry.CommandDispatcher.ImmutableDispatch(initialState, cmd);

            Func<SomethingHappened, SomethingHappened, bool> eventComparer = (a, b) => a.State == b.State;

            Assert.That(newState.State, Is.EqualTo("Foo"));
            Assert.That(events, Is.EquivalentTo(new object[] { new SomethingHappened("Foo") }).Using(eventComparer));
        }

        [Test]
        public void MissingCommandRegistrationThrowsException()
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

            // Create unregistered command to execute on the aggregate.
            var cmd = new DoSomethingElse();

            // Route the events.
            var exception = Assert.Throws<InvalidOperationException>(() => registry.CommandDispatcher.ImmutableDispatch(initialState, cmd));
            const string expectedMessage = "No route found when attempting to apply command " +
                                           "DoSomethingElse to MyAggregateRoot";
            Assert.That(exception.Message, Is.EqualTo(expectedMessage));
        }
        
        [Test]
        public void PreAndPostCommandHooksAreCalled()
        {
            var loggedMessages = new List<string>();
            
            var aggregateRegistryBuilder = AggregateRegistryBuilder.Create<TestCommand, TestEvent>();
            aggregateRegistryBuilder.RegisterPreCommandHook(cmd => loggedMessages.Add($"Pre-command {cmd.Name}"));
            aggregateRegistryBuilder.RegisterPostCommandHook(cmd => loggedMessages.Add($"Post-command {cmd.Name}"));

            aggregateRegistryBuilder.Register<object>(x =>
            {
                x.Command<TestCommand>().RoutesTo((Func<object> _, TestCommand cmd) => new List<TestEvent> {new(cmd.Name)});
                x.Event<TestEvent>().RoutesTo((state, e) =>
                {
                    loggedMessages.Add(e.Name);
                    return state;
                }).HasName("TestEvent");
            });

            aggregateRegistryBuilder.Build().CommandDispatcher.ImmutableDispatch(new object(), new TestCommand("My command"));

            Assert.That(loggedMessages, Is.EquivalentTo(new List<string>
            {
                "Pre-command My command",
                "My command",
                "Post-command My command"
            }));
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
        }

        private class MyAggregateRoot
        {
            public MyAggregateRoot()
            {
            }

            private MyAggregateRoot(string state)
            {
                State = state;
            }

            public string State { get; }

            public IEnumerable<IEvent> ExecuteCommand(Func<MyAggregateRoot> getState, DoSomething command)
            {
                yield return new SomethingHappened(command.State);
            }

            public MyAggregateRoot ApplyEvent(SomethingHappened @event)
            {
                return new MyAggregateRoot(@event.State);
            }
        }
        
        private class TestCommand
        {
            public TestCommand(string name)
            {
                Name = name;
            }

            public string Name { get; }
        }

        public class TestEvent
        {
            public TestEvent(string name)
            {
                Name = name;
            }

            public string Name { get; }
        }
    }
}