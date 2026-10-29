using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit;

public class SubscriptionOptionsTests
{
    private static readonly TimeSpan[] OutOfRangeCatchUpRetryTimeouts =
        [TimeSpan.Zero, TimeSpan.FromTicks(-1), Timeout.InfiniteTimeSpan - TimeSpan.FromTicks(1), TimeSpan.MinValue];

    private static readonly TimeSpan[] BoundaryCatchUpRetryTimeouts =
        [TimeSpan.FromTicks(1), TimeSpan.MaxValue, Timeout.InfiniteTimeSpan];

    [TestCaseSource(nameof(OutOfRangeCatchUpRetryTimeouts))]
    public void CatchUpRetryTimeout_OutOfRange_Throws(TimeSpan retryTimeout)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new SubscriptionOptions { CatchUpRetryTimeout = retryTimeout });
    }

    [TestCaseSource(nameof(BoundaryCatchUpRetryTimeouts))]
    public void CatchUpRetryTimeout_Boundary_IsSet(TimeSpan retryTimeout)
    {
        var options = new SubscriptionOptions { CatchUpRetryTimeout = retryTimeout };

        options.CatchUpRetryTimeout.ShouldBe(retryTimeout);
    }

    [TestCase(-1)]
    [TestCase(int.MinValue)]
    public void CatchUpMaxRetryAttempts_Negative_Throws(int attempts)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new SubscriptionOptions { CatchUpMaxRetryAttempts = attempts });
    }

    [TestCase(0)]
    [TestCase(int.MaxValue)]
    public void CatchUpMaxRetryAttempts_Boundary_IsSet(int attempts)
    {
        var options = new SubscriptionOptions { CatchUpMaxRetryAttempts = attempts };

        options.CatchUpMaxRetryAttempts.ShouldBe(attempts);
    }
}