using DomainBlocks.Infrastructure.MongoDB.Leases;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Infrastructure.MongoDB.Tests.Integration.Leases;

public class LeaseClientTests
{
    private const int TestTimeoutMillis = 30 * 1000;

    private MongoClient _mongoClient = null!;
    private IMongoCollection<LeaseDocument> _leases = null!;
    private ILeaseClient _leaseClient = null!;
    private FakeTimeProvider _fakeTimeProvider = null!;
    private string _resourceId = null!;
    private ILogger<LeaseClient> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        var mongoClient = new MongoClient(MongoConnectionStrings.Default);
        var db = mongoClient.GetDatabase("domainblocks_tests");
        var leases = db.GetCollection<LeaseDocument>("test_leases");

        var fakeTimeProvider = new FakeTimeProvider();

        var leaseStore = new LeaseStore(leases, fakeTimeProvider);

        using var loggerFactory = LoggerFactory.Create(x => x
            .AddConsole()
            .SetMinimumLevel(LogLevel.Debug));

        var logger = loggerFactory.CreateLogger<LeaseClient>();

        _mongoClient = mongoClient;
        _leases = leases;
        _leaseClient = new LeaseClient(leaseStore, logger, fakeTimeProvider);
        _fakeTimeProvider = fakeTimeProvider;
        _resourceId = $"test_resource_{Guid.CreateVersion7():N}";
        _logger = logger;
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _leases.Database.DropCollectionAsync("test_leases");
        _mongoClient.Dispose();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AcquireLeaseAsync_WhenNotAlreadyHeld_ReturnsLease(CancellationToken ct)
    {
        var utcNow = _fakeTimeProvider.GetUtcNow();
        var expectedExpiresAt = utcNow + AcquireLeaseOptions.Default.Duration;

        var result = await _leaseClient.AcquireLeaseAsync(_resourceId, cancellationToken: ct);
        result.IsAcquired.ShouldBeTrue();

        await using var handle = result.Handle;

        handle.Claim.ResourceId.ShouldBe(_resourceId);
        handle.Claim.HolderId.ShouldStartWith(AcquireLeaseOptions.Default.HolderIdPrefix);
        handle.Claim.Epoch.ShouldBe(1);
        handle.CurrentSnapshot.ContentionPriority.ShouldBe(AcquireLeaseOptions.Default.ContentionPriority);
        handle.CurrentSnapshot.LastUpdatedAt.ShouldBe(utcNow);
        handle.CurrentSnapshot.HeldSince.ShouldBe(utcNow);
        handle.CurrentSnapshot.ExpiresAt.ShouldBe(expectedExpiresAt);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AcquireLeaseAsync_WhenAlreadyHeldAndAcquireTimeoutElapsed_IsNotAcquired(CancellationToken ct)
    {
        var result1 = await _leaseClient.AcquireLeaseAsync(_resourceId, cancellationToken: ct);
        result1.IsAcquired.ShouldBeTrue();
        await using var handle1 = result1.Handle;

        var lease2Task = _leaseClient.AcquireLeaseAsync(_resourceId, cancellationToken: ct);

        _fakeTimeProvider.Advance(AcquireLeaseOptions.Default.AcquireTimeout);

        var result2 = await lease2Task.WaitAsync(ct);
        result2.IsAcquired.ShouldBeFalse();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AcquireLeaseAsync_WhenAlreadyHeldWhenAcquireTimeoutIsZero_IsNotAcquired(CancellationToken ct)
    {
        var result1 = await _leaseClient.AcquireLeaseAsync(_resourceId, cancellationToken: ct);
        result1.IsAcquired.ShouldBeTrue();
        await using var handle1 = result1.Handle;

        var options = new AcquireLeaseOptions { AcquireTimeout = TimeSpan.Zero };
        var result2 = await _leaseClient.AcquireLeaseAsync(_resourceId, options, ct);
        result2.IsAcquired.ShouldBeFalse();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AcquireLeaseAsync_AfterLeaseReleased_AllowsNewHolderToAcquire(CancellationToken ct)
    {
        var result1 = await _leaseClient.AcquireLeaseAsync(_resourceId, cancellationToken: ct);
        result1.IsAcquired.ShouldBeTrue();
        string lease1HolderId;

        await using (var handle1 = result1.Handle)
        {
            handle1.Claim.Epoch.ShouldBe(1);
            lease1HolderId = handle1.Claim.HolderId;
        }

        var result2 = await _leaseClient.AcquireLeaseAsync(_resourceId, cancellationToken: ct);

        result2.IsAcquired.ShouldBeTrue();
        await using var handle2 = result2.Handle;
        handle2.Claim.HolderId.ShouldNotBe(lease1HolderId);
        handle2.Claim.Epoch.ShouldBe(2); // We expect epoch to increment on a new acquisition
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AcquireLeaseAsync_WhenHeldButEligibleForPreemption_HigherPriorityAcquires(CancellationToken ct)
    {
        // Avoid renewal happening before min tenure is reached.
        var lease1Options = AcquireLeaseOptions.Default
            .With(x => x.RenewInterval = x.MinTenure + TimeSpan.FromSeconds(5));

        var result1 = await _leaseClient.AcquireLeaseAsync(_resourceId, lease1Options, ct);
        result1.IsAcquired.ShouldBeTrue();
        await using var handle1 = result1.Handle;

        // Make the existing holder eligible for takeover.
        _fakeTimeProvider.Advance(lease1Options.MinTenure);

        // Give new contender a higher contention priority.
        var lease2Options = lease1Options.With(x => x.ContentionPriority++);

        var result2 = await _leaseClient.AcquireLeaseAsync(_resourceId, lease2Options, ct);
        result2.IsAcquired.ShouldBeTrue();
        await using var handle2 = result2.Handle;

        // Advance time so original holder eventually attempts renewal and observes it has lost the lease.
        while (!handle1.LeaseLostTask.IsCompleted)
        {
            ct.ThrowIfCancellationRequested();
            _fakeTimeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        var lostInfo = await handle1.LeaseLostTask.WaitAsync(ct);

        lostInfo.Reason.ShouldBe(LeaseLostReason.Revoked);
        handle1.LeaseLostToken.IsCancellationRequested.ShouldBeTrue();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AcquireLeaseAsync_WhenRenewIntervalElapsed_LeaseIsAutoRenewed(CancellationToken ct)
    {
        var result = await _leaseClient.AcquireLeaseAsync(_resourceId, cancellationToken: ct);
        result.IsAcquired.ShouldBeTrue();
        await using var handle = result.Handle;
        handle.Claim.Epoch.ShouldBe(1);

        var initialExpiry = handle.CurrentSnapshot.ExpiresAt;

        while (handle.CurrentSnapshot.ExpiresAt <= initialExpiry)
        {
            ct.ThrowIfCancellationRequested();
            _fakeTimeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        handle.Claim.Epoch.ShouldBe(1); // Epoch should not change on renewal
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task SchedulePriority_WhenRenewIntervalElapsed_UpdatesPriority(CancellationToken ct)
    {
        var result = await _leaseClient.AcquireLeaseAsync(_resourceId, cancellationToken: ct);
        result.IsAcquired.ShouldBeTrue();
        await using var handle = result.Handle;

        const int newPriority = 10;
        handle.ScheduleContentionPriorityChange(newPriority);
        handle.CurrentSnapshot.ContentionPriority.ShouldBe(0);

        var initialExpiry = handle.CurrentSnapshot.ExpiresAt;

        while (handle.CurrentSnapshot.ExpiresAt <= initialExpiry)
        {
            ct.ThrowIfCancellationRequested();
            _fakeTimeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        handle.CurrentSnapshot.ContentionPriority.ShouldBe(newPriority);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task LeaseLostToken_WhenLeaseExpires_IsCanceled(CancellationToken ct)
    {
        // Force expiry by making the renew-interval longer than the lease duration
        var options = new AcquireLeaseOptions { RenewInterval = AcquireLeaseOptions.Default.Duration * 2 };

        var result = await _leaseClient.AcquireLeaseAsync(_resourceId, options, cancellationToken: ct);
        result.IsAcquired.ShouldBeTrue();
        await using var handle = result.Handle;

        while (!handle.LeaseLostToken.IsCancellationRequested)
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

        var result = await _leaseClient.AcquireLeaseAsync(_resourceId, options, cancellationToken: ct);
        result.IsAcquired.ShouldBeTrue();
        await using var handle = result.Handle;

        while (!handle.LeaseLostTask.IsCompleted)
        {
            ct.ThrowIfCancellationRequested();
            _fakeTimeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        var lostInfo = await handle.LeaseLostTask.WaitAsync(ct);
        lostInfo.Reason.ShouldBe(LeaseLostReason.Revoked);
        lostInfo.Exception.ShouldBeNull();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task LeaseLostTask_WhenLeaseIsDisposed_CompletesWithReasonAsReleased(CancellationToken ct)
    {
        var result = await _leaseClient.AcquireLeaseAsync(_resourceId, cancellationToken: ct);
        result.IsAcquired.ShouldBeTrue();

        await result.Handle.DisposeAsync();

        var lostInfo = await result.Handle.LeaseLostTask.WaitAsync(ct);
        lostInfo.Reason.ShouldBe(LeaseLostReason.Released);
        lostInfo.Exception.ShouldBeNull();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AcquireLeaseAsync_WithMultipleContenders_AllAcquireAndComplete(CancellationToken ct)
    {
        // Don't use FakeTimeProvider - we use real time for this test.
        var leaseStore = new LeaseStore(_leases);
        var leaseClient = new LeaseClient(leaseStore, _logger);

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

                var result = await leaseClient.AcquireLeaseAsync(_resourceId, options, ct);
                result.IsAcquired.ShouldBeTrue();
                await using var handle = result.Handle;

                // Simulate work
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, handle.LeaseLostToken);
                await Task.Delay(20, linkedCts.Token);
            });

        // If all contenders complete successfully before the test timeout, the test succeeds.
        await Task.WhenAll(contenders).WaitAsync(ct);
    }
}