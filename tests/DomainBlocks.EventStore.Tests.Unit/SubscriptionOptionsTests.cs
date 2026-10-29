using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit;

public class SubscriptionOptionsTests
{
    private static readonly TimeSpan[] OutOfRangeRetryTimeouts =
        [TimeSpan.Zero, TimeSpan.FromTicks(-1), Timeout.InfiniteTimeSpan - TimeSpan.FromTicks(1), TimeSpan.MinValue];

    private static readonly TimeSpan[] BoundaryRetryTimeouts =
        [TimeSpan.FromTicks(1), TimeSpan.MaxValue, Timeout.InfiniteTimeSpan];

    [TestCaseSource(nameof(OutOfRangeRetryTimeouts))]
    public void RetryTimeout_OutOfRange_Throws(TimeSpan retryTimeout)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new SubscriptionOptions { RetryTimeout = retryTimeout });
    }

    [TestCaseSource(nameof(BoundaryRetryTimeouts))]
    public void RetryTimeout_Boundary_IsSet(TimeSpan retryTimeout)
    {
        var options = new SubscriptionOptions { RetryTimeout = retryTimeout };

        options.RetryTimeout.ShouldBe(retryTimeout);
    }

    [TestCase(-1)]
    [TestCase(int.MinValue)]
    public void MaxRetryAttempts_Negative_Throws(int attempts)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new SubscriptionOptions { MaxRetryAttempts = attempts });
    }

    [TestCase(0)]
    [TestCase(int.MaxValue)]
    public void MaxRetryAttempts_Boundary_IsSet(int attempts)
    {
        var options = new SubscriptionOptions { MaxRetryAttempts = attempts };

        options.MaxRetryAttempts.ShouldBe(attempts);
    }
}