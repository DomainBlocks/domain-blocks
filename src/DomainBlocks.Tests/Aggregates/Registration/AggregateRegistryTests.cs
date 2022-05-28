using DomainBlocks.Persistence.Builders;
using NUnit.Framework;

namespace DomainBlocks.Tests.Aggregates.Registration
{
    public static class EventNames
    {
        public const string SomethingHappened = "SomethingHappened";
        public const string SomethingElseHappened = "SomethingElseHappened";
    }

    [TestFixture]
    public class AggregateRegistryTests
    {
        [Test]
        public void EventNamesAreMappedWhenEventsAreRegisteredWithName()
        {
            // Setup the aggregate registry.
            var builder = new AggregateRegistryBuilder<object, object>();
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

        private class SomethingHappened
        {
        }

        private class SomethingElseHappened
        {
        }

        private class MyAggregateRoot
        {
            public MyAggregateRoot ApplyEvent(SomethingHappened @event)
            {
                return this;
            }

            public MyAggregateRoot ApplyEvent(SomethingElseHappened @event)
            {
                return this;
            }
        }
    }
}