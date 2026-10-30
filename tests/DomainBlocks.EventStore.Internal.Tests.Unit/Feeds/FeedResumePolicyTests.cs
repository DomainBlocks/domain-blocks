using DomainBlocks.EventStore.Internal.Feeds;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Internal.Tests.Unit.Feeds;

public class FeedResumePolicyTests
{
    [Test]
    public void Constructor_RetryDelayOverOneDay_Throws()
    {
        var options = new FeedResumeOptions { RetryDelay = TimeSpan.FromDays(1) + TimeSpan.FromTicks(1) };

        Should.Throw<ArgumentOutOfRangeException>(() => new FeedResumePolicy(static _ => true, options));
    }

    [Test]
    public void Constructor_MaxRetryDelayOverOneDay_Throws()
    {
        var options = new FeedResumeOptions { MaxRetryDelay = TimeSpan.FromDays(1) + TimeSpan.FromTicks(1) };

        Should.Throw<ArgumentOutOfRangeException>(() => new FeedResumePolicy(static _ => true, options));
    }

    [Test]
    public void Constructor_DelaysOfOneDay_Succeeds()
    {
        var options = new FeedResumeOptions { RetryDelay = TimeSpan.FromDays(1), MaxRetryDelay = TimeSpan.FromDays(1) };

        Should.NotThrow(() => new FeedResumePolicy(static _ => true, options));
    }
}