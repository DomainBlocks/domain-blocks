using System;
using DomainLib.Aggregates;
using NUnit.Framework;

namespace DomainLib.Tests.Aggregates
{
    [TestFixture]
    public class EventNameMapTests
    {
        public const string AttributeEventName = "AttributeEventName";
        public const string DerivedAttributeEventName = "DerviedAttributeEventName";
        public const string ConstantEventName= "ConstantEventName";

        [Test]
        public void EventNameIsSelectedFromAttribute()
        {
            VerifyEventNameForEvent(typeof(OnlyAttributeEvent), AttributeEventName);
        }

        [Test]
        public void EventNameIsSelectedFromAttributeWhenConstantIsPresent()
        {
            VerifyEventNameForEvent(typeof(AttributeAndConstantEvent), AttributeEventName);
        }

        [Test]
        public void EventNameIsSelectedFromConstantWhenNoAttribute()
        {
            VerifyEventNameForEvent(typeof(OnlyConstantEvent), ConstantEventName);
        }

        [Test]
        public void EventNameIsSelectedFromClassNameWhenNoAttributeOrConstant()
        {
            VerifyEventNameForEvent(typeof(OnlyClassNameEvent), nameof(OnlyClassNameEvent));
        }

        [Test]
        public void AttributeOverridesConstantInDerivedClass()
        {
            VerifyEventNameForEvent(typeof(DerivedEvent), DerivedAttributeEventName);
        }

        [Test]
        public void EventNameIsSelectedCorrectlyForDerivedClass2()
        {
            VerifyEventNameForEvent(typeof(DerivedEventWithConstantOnly), AttributeEventName);
        }

        [Test]
        public void RegisteringTwoEventsWithTheSameNameThrows()
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                var eventNameMap = new EventNameMap();
                eventNameMap.RegisterEvent<OnlyAttributeEvent>();
                eventNameMap.RegisterEvent<OnlyAttributeEvent2>();
            });
        }

        [Test]
        public void CanOverrideEventRegistrationIfUserChoosesNotToThrow()
        {
            var eventNameMap = new EventNameMap();
            eventNameMap.RegisterEvent<OnlyAttributeEvent>();
            eventNameMap.RegisterEvent<OnlyAttributeEvent2>(throwOnConflict: false);

            Assert.That(eventNameMap.GetClrTypeForEventName(AttributeEventName),
                        Is.EqualTo(typeof(OnlyAttributeEvent2)));
        }

        [Test]
        public void CanMergeEventNameMaps()
        {
            var eventNameMap1 = new EventNameMap();
            var eventNameMap2 = new EventNameMap();

            eventNameMap1.RegisterEvent<OnlyAttributeEvent>();
            eventNameMap2.RegisterEvent<OnlyConstantEvent>();

            eventNameMap1.Merge(eventNameMap2);

            Assert.That(eventNameMap1.GetEventNameForClrType(typeof(OnlyAttributeEvent)),
                        Is.EqualTo(AttributeEventName));
            
            Assert.That(eventNameMap1.GetEventNameForClrType(typeof(OnlyConstantEvent)),
                        Is.EqualTo(ConstantEventName));
        }

        private static void VerifyEventNameForEvent(Type eventType, string expectedEventName)
        {
            var eventNameMap = new EventNameMap();
            eventNameMap.RegisterEvent(eventType);

            Assert.That(eventNameMap.GetEventNameForClrType(eventType),
                        Is.EqualTo(expectedEventName));
        }
    }

    [EventName(EventNameMapTests.AttributeEventName)]
    public class OnlyAttributeEvent
    {
    }

    [EventName(EventNameMapTests.AttributeEventName)]
    public class OnlyAttributeEvent2
    {
    }

    [EventName(EventNameMapTests.AttributeEventName)]
    public class AttributeAndConstantEvent
    {
        public const string EventName = EventNameMapTests.ConstantEventName;
    }

    public class OnlyConstantEvent
    {
        public const string EventName = EventNameMapTests.ConstantEventName;
    }

    public class OnlyClassNameEvent
    {
    }

    [EventName(EventNameMapTests.DerivedAttributeEventName)]
    public class DerivedEvent : OnlyAttributeEvent
    {
    }

    public class DerivedEventWithConstantOnly : OnlyAttributeEvent
    {
        public const string EventName = EventNameMapTests.ConstantEventName;
    }
}