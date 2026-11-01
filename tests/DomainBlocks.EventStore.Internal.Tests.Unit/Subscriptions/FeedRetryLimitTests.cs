using DomainBlocks.EventStore.Internal.Feeds;
using DomainBlocks.EventStore.Internal.Subscriptions;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Internal.Tests.Unit.Subscriptions;

public class FeedRetryLimitTests
{
    private static readonly TimeSpan NoTimeout = Timeout.InfiniteTimeSpan;

    [Test]
    public void IsReachedBy_ZeroRetries_ReachedByFirstFailure()
    {
        var limit = new FeedRetryLimit(0, NoTimeout);

        limit.IsReachedBy(Failure(1)).ShouldBeTrue();
    }

    [Test]
    public void IsReachedBy_TwoRetries_ReachedByThirdFailure()
    {
        var limit = new FeedRetryLimit(2, NoTimeout);

        limit.IsReachedBy(Failure(1)).ShouldBeFalse();
        limit.IsReachedBy(Failure(2)).ShouldBeFalse();
        limit.IsReachedBy(Failure(3)).ShouldBeTrue();
    }

    [Test]
    public void IsReachedBy_UnlimitedRetries_NeverReached()
    {
        var limit = new FeedRetryLimit(int.MaxValue, NoTimeout);

        limit.IsReachedBy(Failure(1)).ShouldBeFalse();
        limit.IsReachedBy(Failure(int.MaxValue, TimeSpan.FromDays(365))).ShouldBeFalse();
    }

    [Test]
    public void IsReachedBy_Timeout_ReachedOnceTimeHasPassed()
    {
        var limit = new FeedRetryLimit(int.MaxValue, TimeSpan.FromMinutes(5));

        limit.IsReachedBy(Failure(1)).ShouldBeFalse();
        limit.IsReachedBy(Failure(2, TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(1))).ShouldBeFalse();
        limit.IsReachedBy(Failure(3, TimeSpan.FromMinutes(5))).ShouldBeTrue();
    }

    [Test]
    public void IsReachedBy_JoinsOutageLate_GetsWholeLimits()
    {
        var limit = new FeedRetryLimit(2, TimeSpan.FromMinutes(5));

        limit.IsReachedBy(Failure(10, TimeSpan.FromMinutes(20))).ShouldBeFalse();
        limit.IsReachedBy(Failure(11, TimeSpan.FromMinutes(21))).ShouldBeFalse();
        limit.IsReachedBy(Failure(12, TimeSpan.FromMinutes(22))).ShouldBeTrue();
    }

    [Test]
    public void IsReachedBy_JoinsOutageLate_TimesFromFirstFailureSeen()
    {
        var limit = new FeedRetryLimit(int.MaxValue, TimeSpan.FromMinutes(5));

        limit.IsReachedBy(Failure(10, TimeSpan.FromMinutes(20))).ShouldBeFalse();
        limit.IsReachedBy(Failure(11, TimeSpan.FromMinutes(24))).ShouldBeFalse();
        limit.IsReachedBy(Failure(12, TimeSpan.FromMinutes(25))).ShouldBeTrue();
    }

    [Test]
    public void IsReachedBy_NewOutage_StartsAgain()
    {
        var limit = new FeedRetryLimit(1, TimeSpan.FromMinutes(5));

        limit.IsReachedBy(Failure(1)).ShouldBeFalse();

        // A session stayed up for long enough, so the feed recovered between these two.
        limit.IsReachedBy(Failure(1)).ShouldBeFalse();
        limit.IsReachedBy(Failure(2)).ShouldBeTrue();
    }

    [Test]
    public void IsReachedBy_NewOutageAfterLongerOne_StartsCountAgain()
    {
        var limit = new FeedRetryLimit(1, NoTimeout);

        limit.IsReachedBy(Failure(5)).ShouldBeFalse();
        limit.IsReachedBy(Failure(1)).ShouldBeFalse();
        limit.IsReachedBy(Failure(2)).ShouldBeTrue();
    }

    [Test]
    public void IsReachedBy_NewOutageAfterLongerOne_StartsTimeAgain()
    {
        var limit = new FeedRetryLimit(int.MaxValue, TimeSpan.FromMinutes(5));

        limit.IsReachedBy(Failure(5, TimeSpan.FromMinutes(20))).ShouldBeFalse();
        limit.IsReachedBy(Failure(1)).ShouldBeFalse();
        limit.IsReachedBy(Failure(2, TimeSpan.FromMinutes(5))).ShouldBeTrue();
    }

    private static FeedFailure Failure(int failureCount, TimeSpan outageDuration = default) =>
        new(new IOException("lost"), failureCount, outageDuration, hasConnected: true);
}