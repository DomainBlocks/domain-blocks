using DomainBlocks.Infrastructure.MongoDB.Leases;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Infrastructure.MongoDB.Tests.Integration.Leases;

public class LeaseProviderTests
{
    private const int TestTimeoutMillis = 30 * 1000;

    private MongoClient _mongoClient = null!;
    private IMongoCollection<LeaseState> _leaseStates = null!;
    private ILeaseProvider _leaseProvider = null!;
    private FakeTimeProvider _fakeTimeProvider = null!;
    private string _resourceId = null!;
    private ILogger<LeaseProvider> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        var mongoClient = new MongoClient(MongoConnectionStrings.Default);
        var db = mongoClient.GetDatabase("domainblocks_tests");
        var leaseStates = db.GetCollection<LeaseState>("test_leases");

        var fakeTimeProvider = new FakeTimeProvider();

        var leaseStore = new LeaseStore(leaseStates, fakeTimeProvider);

        using var loggerFactory = LoggerFactory.Create(x => x
            .AddConsole()
            .SetMinimumLevel(LogLevel.Debug));

        var logger = loggerFactory.CreateLogger<LeaseProvider>();

        _mongoClient = mongoClient;
        _leaseStates = leaseStates;
        _leaseProvider = new LeaseProvider(leaseStore, logger, fakeTimeProvider);
        _fakeTimeProvider = fakeTimeProvider;
        _resourceId = $"test_resource_{Guid.CreateVersion7():N}";
        _logger = logger;
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _leaseStates.Database.DropCollectionAsync("test_leases");
        _mongoClient.Dispose();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AcquireLeaseAsync_WhenNotAlreadyHeld_ReturnsLease(CancellationToken ct)
    {
        var utcNow = _fakeTimeProvider.GetUtcNow();
        var expectedExpiresAt = utcNow + AcquireLeaseOptions.Default.Duration;

        await using var lease = await _leaseProvider.AcquireLeaseAsync(_resourceId, cancellationToken: ct);

        lease.IsAcquired.ShouldBeTrue();
        lease.Handle.Claim.ResourceId.ShouldBe(_resourceId);
        lease.Handle.Claim.HolderId.ShouldStartWith(AcquireLeaseOptions.Default.HolderIdPrefix);
        lease.Handle.Claim.Epoch.ShouldBe(1);
        lease.Handle.ContentionPriority.ShouldBe(AcquireLeaseOptions.Default.ContentionPriority);
        lease.Handle.UpdatedAt.ShouldBe(utcNow);
        lease.Handle.HeldSince.ShouldBe(utcNow);
        lease.Handle.ExpiresAt.ShouldBe(expectedExpiresAt);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AcquireLeaseAsync_WhenAlreadyHeldAndAcquireTimeoutElapsed_IsNotAcquired(CancellationToken ct)
    {
        await using var lease1 = await _leaseProvider.AcquireLeaseAsync(_resourceId, cancellationToken: ct);
        lease1.IsAcquired.ShouldBeTrue();

        var lease2Task = _leaseProvider.AcquireLeaseAsync(_resourceId, cancellationToken: ct);

        _fakeTimeProvider.Advance(AcquireLeaseOptions.Default.AcquireTimeout);

        await using var lease2 = await lease2Task.WaitAsync(ct);
        lease2.IsAcquired.ShouldBeFalse();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AcquireLeaseAsync_WhenAlreadyHeldWhenAcquireTimeoutIsZero_IsNotAcquired(CancellationToken ct)
    {
        await using var lease1 = await _leaseProvider.AcquireLeaseAsync(_resourceId, cancellationToken: ct);
        lease1.IsAcquired.ShouldBeTrue();

        var options = new AcquireLeaseOptions { AcquireTimeout = TimeSpan.Zero };
        await using var lease2 = await _leaseProvider.AcquireLeaseAsync(_resourceId, options, ct);
        lease2.IsAcquired.ShouldBeFalse();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AcquireLeaseAsync_AfterLeaseReleased_AllowsNewHolderToAcquire(CancellationToken ct)
    {
        string lease1HolderId;

        await using (var lease1 = await _leaseProvider.AcquireLeaseAsync(_resourceId, cancellationToken: ct))
        {
            lease1.IsAcquired.ShouldBeTrue();
            lease1.Handle.Claim.Epoch.ShouldBe(1);
            lease1HolderId = lease1.Handle.Claim.HolderId;
        }

        await using var lease2 = await _leaseProvider.AcquireLeaseAsync(_resourceId, cancellationToken: ct);
        lease2.IsAcquired.ShouldBeTrue();
        lease2.Handle.Claim.HolderId.ShouldNotBe(lease1HolderId);
        lease2.Handle.Claim.Epoch.ShouldBe(2); // We expect epoch to increment on a new acquisition
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AcquireLeaseAsync_WhenHeldButEligibleForPreemption_HigherPriorityAcquires(CancellationToken ct)
    {
        // Avoid renewal happening before min tenure is reached.
        var lease1Options = AcquireLeaseOptions.Default
            .With(x => x.RenewInterval = x.MinTenure + TimeSpan.FromSeconds(5));

        await using var lease1 = await _leaseProvider.AcquireLeaseAsync(_resourceId, lease1Options, ct);
        lease1.IsAcquired.ShouldBeTrue();

        // Make the existing holder eligible for takeover.
        _fakeTimeProvider.Advance(lease1Options.MinTenure);

        // Give new contender a higher contention priority.
        var lease2Options = lease1Options.With(x => x.ContentionPriority++);

        await using var lease2 = await _leaseProvider.AcquireLeaseAsync(_resourceId, lease2Options, ct);
        lease2.IsAcquired.ShouldBeTrue();

        // Advance time so original holder eventually attempts renewal and observes it has lost the lease.
        while (!lease1.Handle.LeaseLostTask.IsCompleted)
        {
            ct.ThrowIfCancellationRequested();
            _fakeTimeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        var lostInfo = await lease1.Handle.LeaseLostTask.WaitAsync(ct);

        lostInfo.Reason.ShouldBe(LeaseLostReason.Revoked);
        lease1.Handle.LeaseLostToken.IsCancellationRequested.ShouldBeTrue();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AcquireLeaseAsync_WhenRenewIntervalElapsed_LeaseIsAutoRenewed(CancellationToken ct)
    {
        await using var lease = await _leaseProvider.AcquireLeaseAsync(_resourceId, cancellationToken: ct);
        lease.IsAcquired.ShouldBeTrue();
        lease.Handle.Claim.Epoch.ShouldBe(1);

        var initialExpiry = lease.Handle.ExpiresAt;

        while (lease.Handle.ExpiresAt <= initialExpiry)
        {
            ct.ThrowIfCancellationRequested();
            _fakeTimeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        lease.Handle.Claim.Epoch.ShouldBe(1); // Epoch should not change on renewal
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task SchedulePriority_WhenRenewIntervalElapsed_UpdatesPriority(CancellationToken ct)
    {
        await using var lease = await _leaseProvider.AcquireLeaseAsync(_resourceId, cancellationToken: ct);
        lease.IsAcquired.ShouldBeTrue();

        const int newPriority = 10;
        lease.Handle.ScheduleContentionPriorityChange(newPriority);
        lease.Handle.ContentionPriority.ShouldBe(0);

        var initialExpiry = lease.Handle.ExpiresAt;

        while (lease.Handle.ExpiresAt <= initialExpiry)
        {
            ct.ThrowIfCancellationRequested();
            _fakeTimeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        lease.Handle.ContentionPriority.ShouldBe(newPriority);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task LeaseLostToken_WhenLeaseExpires_IsCanceled(CancellationToken ct)
    {
        // Force expiry by making the renew-interval longer than the lease duration
        var options = new AcquireLeaseOptions { RenewInterval = AcquireLeaseOptions.Default.Duration * 2 };

        await using var lease = await _leaseProvider.AcquireLeaseAsync(_resourceId, options, cancellationToken: ct);
        lease.IsAcquired.ShouldBeTrue();

        while (!lease.Handle.LeaseLostToken.IsCancellationRequested)
        {
            ct.ThrowIfCancellationRequested();
            _fakeTimeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task LeaseLostTask_WhenLeaseExpires_CompletesWithReasonAsRevoked(CancellationToken ct)
    {
        // Force expiry by making the renew-interval longer than the lease duration
        var options = new AcquireLeaseOptions { RenewInterval = AcquireLeaseOptions.Default.Duration * 2 };

        await using var lease = await _leaseProvider.AcquireLeaseAsync(_resourceId, options, cancellationToken: ct);
        lease.IsAcquired.ShouldBeTrue();

        while (!lease.Handle.LeaseLostTask.IsCompleted)
        {
            ct.ThrowIfCancellationRequested();
            _fakeTimeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        var lostInfo = await lease.Handle.LeaseLostTask.WaitAsync(ct);
        lostInfo.Reason.ShouldBe(LeaseLostReason.Revoked);
        lostInfo.Exception.ShouldBeNull();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task LeaseLostTask_WhenLeaseIsDisposed_CompletesWithReasonAsReleased(CancellationToken ct)
    {
        LeaseAcquisition lease;

        await using (lease = await _leaseProvider.AcquireLeaseAsync(_resourceId, cancellationToken: ct))
        {
            lease.IsAcquired.ShouldBeTrue();
        }

        var lostInfo = await lease.Handle.LeaseLostTask.WaitAsync(ct);
        lostInfo.Reason.ShouldBe(LeaseLostReason.Released);
        lostInfo.Exception.ShouldBeNull();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AcquireLeaseAsync_WithMultipleContenders_AllAcquireAndComplete(CancellationToken ct)
    {
        // Don't use FakeTimeProvider - we use real time for this test.
        var leaseStore = new LeaseStore(_leaseStates);
        var leaseProvider = new LeaseProvider(leaseStore, _logger);

        var contenders = Enumerable
            .Range(0, 5)
            .Select(async i =>
            {
                var options = new AcquireLeaseOptions
                {
                    HolderIdPrefix = $"contender{i}",
                    AcquireTimeout = Timeout.InfiniteTimeSpan,
                    // Retry more aggressively so the test is fast
                    AcquireRetryDelay = TimeSpan.FromMilliseconds(100)
                };

                await using var lease = await leaseProvider.AcquireLeaseAsync(_resourceId, options, ct);

                lease.IsAcquired.ShouldBeTrue();

                // Simulate work
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, lease.Handle.LeaseLostToken);
                await Task.Delay(20, linkedCts.Token);
            });

        // If all contenders complete successfully before the test timeout, the test succeeds.
        await Task.WhenAll(contenders).WaitAsync(ct);
    }
}