using DomainBlocks.EventStore.Tests.Unit.Decoration;
using NUnit.Framework;
using Shouldly;
using Message = DomainBlocks.EventStore.SubscriptionMessage<
    object,
    string,
    DomainBlocks.EventStore.StreamPosition,
    DomainBlocks.EventStore.LogPosition>;
using StoredEvent = DomainBlocks.EventStore.ReadEvent<
    object,
    string,
    DomainBlocks.EventStore.StreamPosition,
    DomainBlocks.EventStore.LogPosition>;

namespace DomainBlocks.EventStore.Tests.Unit;

public class SubscriptionMessageTests
{
    [Test]
    public void Instance_WhenDefaultConstructed_HoldsNoMessage()
    {
        var message = default(Message);

        (message is null).ShouldBeTrue();
        message.HasValue.ShouldBeFalse();
        message.Value.ShouldBeNull();
        message.ToString().ShouldBe("None");
    }

    [Test]
    public void Conversion_FromReadEvent_IsEventCase()
    {
        var readEvent = FakeEventStore.ReadEventAt("payload", 7);

        Message message = readEvent;

        (message is StoredEvent e && ReferenceEquals(e.Payload, readEvent.Payload)).ShouldBeTrue();
        (message is SubscriptionCaughtUp).ShouldBeFalse();
        message.HasValue.ShouldBeTrue();
        message.Value.ShouldBeOfType<StoredEvent>().Context.ShouldBe(readEvent.Context);
    }

    [Test]
    public void Conversion_FromCaughtUp_IsCaughtUpCase()
    {
        Message message = SubscriptionMessage.CaughtUp;

        (message is SubscriptionCaughtUp).ShouldBeTrue();
        (message is SubscriptionFellBehind).ShouldBeFalse();
        message.Value.ShouldBeOfType<SubscriptionCaughtUp>();
        message.ToString().ShouldBe("CaughtUp");
    }

    [Test]
    public void Conversion_FromFellBehind_IsFellBehindCase()
    {
        Message message = SubscriptionMessage.FellBehind;

        (message is SubscriptionFellBehind).ShouldBeTrue();
        (message is SubscriptionCaughtUp).ShouldBeFalse();
        message.Value.ShouldBeOfType<SubscriptionFellBehind>();
        message.ToString().ShouldBe("FellBehind");
    }

    [Test]
    public void TryGetValue_WhenEvent_ReturnsOnlyEvent()
    {
        Message message = FakeEventStore.ReadEventAt("payload", 7);

        message.TryGetValue(out StoredEvent readEvent).ShouldBeTrue();
        readEvent.Payload.ShouldBe("payload");
        message.TryGetValue(out SubscriptionCaughtUp _).ShouldBeFalse();
        message.TryGetValue(out SubscriptionFellBehind _).ShouldBeFalse();
    }

    [Test]
    public void TryGetValue_WhenCaughtUp_ReturnsOnlyCaughtUp()
    {
        Message message = SubscriptionMessage.CaughtUp;

        message.TryGetValue(out SubscriptionCaughtUp _).ShouldBeTrue();
        message.TryGetValue(out StoredEvent _).ShouldBeFalse();
        message.TryGetValue(out SubscriptionFellBehind _).ShouldBeFalse();
    }

    [Test]
    public void Event_WhenEvent_ReturnsEvent()
    {
        Message message = FakeEventStore.ReadEventAt("payload", 7);

        message.Event.ShouldNotBeNull().Payload.ShouldBe("payload");
    }

    [Test]
    public void Event_WhenNotAnEvent_ReturnsNull()
    {
        Message caughtUp = SubscriptionMessage.CaughtUp;
        Message fellBehind = SubscriptionMessage.FellBehind;

        caughtUp.Event.ShouldBeNull();
        fellBehind.Event.ShouldBeNull();
        default(Message).Event.ShouldBeNull();
    }

    // The shorthand is a property pattern, which applies to the message itself, and it may sit beside case patterns,
    // which apply to the message's value.
    [Test]
    public void Switch_WithEventPropertyBesideCasePatterns_MatchesEachMessage()
    {
        Describe(FakeEventStore.ReadEventAt("payload", 7)).ShouldBe("event:payload");
        Describe(SubscriptionMessage.CaughtUp).ShouldBe("caught up");
        Describe(SubscriptionMessage.FellBehind).ShouldBe("fell behind");
        Describe(default).ShouldBe("none");
    }

    // The compiler silently falls back to the boxing Value property if TryGetValue ever stops matching the
    // non-boxing access pattern, so only an allocation check can catch that. ReadEvent is a struct, and a
    // subscription delivers one message per event.
    [Test]
    public void Switch_WhenEvent_DoesNotAllocate()
    {
        Message message = FakeEventStore.ReadEventAt("payload", 7);
        PositionOf(message);

        var before = GC.GetAllocatedBytesForCurrentThread();
        ulong total = 0;

        for (var i = 0; i < 1_000; i++)
            total += PositionOf(message);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        total.ShouldBe(7_000ul);
        allocated.ShouldBe(0);
    }

    private static string Describe(Message message)
    {
        // The discard is needed: a pattern on Event is not a pattern on the event case, so without it the compiler
        // reports the switch as not exhaustive.
        return message switch
        {
            { Event: { } e } => $"event:{e.Payload}",
            SubscriptionCaughtUp => "caught up",
            SubscriptionFellBehind => "fell behind",
            _ => "none"
        };
    }

    // Exhaustive without a discard: the compiler knows every case of the union.
    private static ulong PositionOf(Message message)
    {
        return message switch
        {
            StoredEvent e => e.Context.StreamPosition.Value,
            SubscriptionCaughtUp => 0,
            SubscriptionFellBehind => 0,
            null => 0
        };
    }
}