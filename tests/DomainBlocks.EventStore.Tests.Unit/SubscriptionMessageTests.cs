using System.Runtime.CompilerServices;
using NUnit.Framework;
using Shouldly;
using Message = DomainBlocks.EventStore.SubscriptionMessage<
    object,
    string,
    DomainBlocks.EventStore.StreamPosition,
    DomainBlocks.EventStore.LogPosition>;

namespace DomainBlocks.EventStore.Tests.Unit;

public class SubscriptionMessageTests
{
    [Test]
    public void LogCheckpoint_WhenCreated_CarriesTheLogPositionAlone()
    {
        var message = SubscriptionMessage.LogCheckpoint<object, string, StreamPosition, LogPosition>(
            LogPosition.FromInt64(42));

        message.Kind.ShouldBe(SubscriptionMessageKind.Checkpoint);
        message.IsCheckpoint.ShouldBeTrue();
        message.LogCheckpoint.HasValue.ShouldBeTrue();
        message.LogCheckpoint.Value.ShouldBe(LogPosition.FromInt64(42));
        message.StreamCheckpoint.HasValue.ShouldBeFalse();
        message.Event.ShouldBeNull();
    }

    [Test]
    public void StreamCheckpoint_WhenCreated_CarriesTheStreamPositionAlone()
    {
        var message = SubscriptionMessage.StreamCheckpoint<object, string, StreamPosition, LogPosition>(
            StreamPosition.FromInt64(7));

        message.IsCheckpoint.ShouldBeTrue();
        message.StreamCheckpoint.HasValue.ShouldBeTrue();
        message.StreamCheckpoint.Value.ShouldBe(StreamPosition.FromInt64(7));
        message.LogCheckpoint.HasValue.ShouldBeFalse();
        message.Event.ShouldBeNull();
    }

    [Test]
    public void Checkpoint_WhenAtTheFirstPosition_StillHasAValue()
    {
        SubscriptionMessage.LogCheckpoint<object, string, StreamPosition, LogPosition>(LogPosition.FromInt64(0))
            .LogCheckpoint.HasValue.ShouldBeTrue();

        SubscriptionMessage.StreamCheckpoint<object, string, StreamPosition, LogPosition>(StreamPosition.FromInt64(0))
            .StreamCheckpoint.HasValue.ShouldBeTrue();
    }

    [Test]
    public void Checkpoints_WhenTheMessageIsOfAnotherKind_HaveNoValue()
    {
        var context = ReadEventContext.Create(
            "s",
            "E",
            new Dictionary<string, string>(),
            DateTimeOffset.UnixEpoch,
            StreamPosition.FromInt64(3),
            LogPosition.FromInt64(9));

        var e = ReadEvent.Create<object, string, StreamPosition, LogPosition>("e", context);
        Message[] messages = [Message.CaughtUp, Message.FellBehind, SubscriptionMessage.Event(e)];

        foreach (var message in messages)
        {
            message.IsCheckpoint.ShouldBeFalse();
            message.LogCheckpoint.HasValue.ShouldBeFalse();
            message.StreamCheckpoint.HasValue.ShouldBeFalse();
        }
    }

    [Test]
    public void ToString_WhenACheckpoint_SaysOfWhatAndWhere()
    {
        SubscriptionMessage.LogCheckpoint<object, string, StreamPosition, LogPosition>(LogPosition.FromInt64(42))
            .ToString().ShouldBe($"Checkpoint(log@{LogPosition.FromInt64(42)})");

        SubscriptionMessage.StreamCheckpoint<object, string, StreamPosition, LogPosition>(StreamPosition.FromInt64(7))
            .ToString().ShouldBe($"Checkpoint(stream@{StreamPosition.FromInt64(7)})");
    }

    [Test]
    public void Size_WhenAbleToCarryACheckpoint_IsThatOfAKindAndAnEvent()
    {
        // Subscriptions queue messages, so every message would pay for a checkpoint that had fields of its own.
        Unsafe.SizeOf<Message>().ShouldBe(Unsafe.SizeOf<KindAndEvent>());
    }

    [Test]
    public void CheckpointInterval_WhenNotPositive_Throws()
    {
        SubscriptionOptions.Default.CheckpointInterval.ShouldBe(TimeSpan.FromSeconds(1));

        Should.Throw<ArgumentOutOfRangeException>(() => new SubscriptionOptions { CheckpointInterval = TimeSpan.Zero });
    }

#pragma warning disable CS0649 // Only its size matters.
    private struct KindAndEvent
    {
        public SubscriptionMessageKind Kind;
        public ReadEvent<object, string, StreamPosition, LogPosition> Event;
    }
#pragma warning restore CS0649
}