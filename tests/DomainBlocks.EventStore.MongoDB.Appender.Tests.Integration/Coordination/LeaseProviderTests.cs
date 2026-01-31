using DomainBlocks.EventStore.MongoDB.Appender.Coordination;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Appender.Tests.Integration.Coordination;

public class LeaseProviderTests
{
    private const int TestTimeoutMillis = 30 * 1000;

    private FakeTimeProvider _fakeTimeProvider = null!;
    private IMongoCollection<LeaseState> _leaseStates = null!;
    private ILeaseProvider _leaseProvider = null!;
    private string _resourceId = null!;
    private ILogger<LeaseProvider> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        var fakeTimeProvider = new FakeTimeProvider();

        var mongoClient = new MongoClient(MongoConnectionStrings.Default);
        var db = mongoClient.GetDatabase("domainblocks");
        var leaseStates = db.GetCollection<LeaseState>("es_leases");

        var leaseStore = new LeaseStore(leaseStates, fakeTimeProvider);

        using var loggerFactory = LoggerFactory.Create(x => x
            .AddConsole()
            .SetMinimumLevel(LogLevel.Debug));

        var logger = loggerFactory.CreateLogger<LeaseProvider>();

        _fakeTimeProvider = fakeTimeProvider;
        _leaseStates = leaseStates;
        _leaseProvider = new LeaseProvider(leaseStore, logger, fakeTimeProvider);
        _resourceId = $"test_resource_{Guid.CreateVersion7():N}";
        _logger = logger;
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _leaseStates.Database.DropCollectionAsync("es_leases");
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AcquireLeaseAsync_WhenNotAlreadyHeld_ReturnsLease(CancellationToken ct)
    {
        await using var lease = await _leaseProvider.AcquireLeaseAsync(_resourceId, cancellationToken: ct);

        lease.ShouldNotBeNull();
        lease.ResourceId.ShouldBe(_resourceId);
        lease.HolderId.ShouldStartWith(AcquireLeaseOptions.Default.HolderIdPrefix);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AcquireLeaseAsync_WhenAlreadyHeldAndAcquireTimeoutElapsed_ReturnsNull(CancellationToken ct)
    {
        await using var lease1 = await _leaseProvider.AcquireLeaseAsync(_resourceId, cancellationToken: ct);
        lease1.ShouldNotBeNull();

        var lease2Task = _leaseProvider.AcquireLeaseAsync(_resourceId, cancellationToken: ct);

        _fakeTimeProvider.Advance(AcquireLeaseOptions.Default.AcquireTimeout);

        await using var lease2 = await lease2Task;
        lease2.ShouldBeNull();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AcquireLeaseAsync_WhenAlreadyHeldWhenAcquireTimeoutIsZero_ReturnsNull(CancellationToken ct)
    {
        await using var lease1 = await _leaseProvider.AcquireLeaseAsync(_resourceId, cancellationToken: ct);
        lease1.ShouldNotBeNull();

        var options = new AcquireLeaseOptions { AcquireTimeout = TimeSpan.Zero };
        await using var lease2 = await _leaseProvider.AcquireLeaseAsync(_resourceId, options, ct);
        lease2.ShouldBeNull();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AcquireLeaseAsync_AfterLeaseReleased_AllowsNewHolderToAcquire(CancellationToken ct)
    {
        await using (var lease1 = await _leaseProvider.AcquireLeaseAsync(_resourceId, cancellationToken: ct))
        {
            lease1.ShouldNotBeNull();
        }

        await using var lease2 = await _leaseProvider.AcquireLeaseAsync(_resourceId, cancellationToken: ct);
        lease2.ShouldNotBeNull();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AcquireLeaseAsync_WhenRenewIntervalElapsed_LeaseIsAutoRenewed(CancellationToken ct)
    {
        await using var lease = await _leaseProvider.AcquireLeaseAsync(_resourceId, cancellationToken: ct);
        lease.ShouldNotBeNull();

        var initialExpiry = lease.ExpiresAtUtc;

        _fakeTimeProvider.Advance(AcquireLeaseOptions.Default.RenewInterval);

        while (lease.ExpiresAtUtc <= initialExpiry)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task SchedulePriority_WhenRenewIntervalElapsed_UpdatesPriority(CancellationToken ct)
    {
        await using var lease = await _leaseProvider.AcquireLeaseAsync(_resourceId, cancellationToken: ct);
        lease.ShouldNotBeNull();

        const int newPriority = 10;
        lease.ScheduleContentionPriorityChange(newPriority);
        lease.ContentionPriority.ShouldBe(0);

        var initialExpiry = lease.ExpiresAtUtc;

        _fakeTimeProvider.Advance(AcquireLeaseOptions.Default.RenewInterval);

        while (lease.ExpiresAtUtc <= initialExpiry)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
        }

        lease.ContentionPriority.ShouldBe(newPriority);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task LeaseLostToken_WhenLeaseExpires_IsCanceled(CancellationToken ct)
    {
        // Force expiry by making the renew-interval longer than the lease duration
        var options = new AcquireLeaseOptions { RenewInterval = AcquireLeaseOptions.Default.Duration * 2 };

        await using var lease = await _leaseProvider.AcquireLeaseAsync(_resourceId, options, cancellationToken: ct);
        lease.ShouldNotBeNull();

        _fakeTimeProvider.Advance(options.RenewInterval);

        var tcs = new TaskCompletionSource();
        await using var registration = lease.LeaseLostToken.Register(tcs.SetResult);
        await tcs.Task.WaitAsync(ct);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task LeaseLostTask_WhenLeaseExpires_CompletesWithReasonAsRevoked(CancellationToken ct)
    {
        // Force expiry by making the renew-interval longer than the lease duration
        var options = new AcquireLeaseOptions { RenewInterval = AcquireLeaseOptions.Default.Duration * 2 };

        await using var lease = await _leaseProvider.AcquireLeaseAsync(_resourceId, options, cancellationToken: ct);
        lease.ShouldNotBeNull();

        _fakeTimeProvider.Advance(options.RenewInterval);

        var lostInfo = await lease.LeaseLostTask.WaitAsync(ct);
        lostInfo.Reason.ShouldBe(LeaseLostReason.Revoked);
        lostInfo.Exception.ShouldBeNull();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task LeaseLostTask_WhenLeaseIsDisposed_CompletesWithReasonAsReleased(CancellationToken ct)
    {
        ILease? lease;

        await using (lease = await _leaseProvider.AcquireLeaseAsync(_resourceId, cancellationToken: ct))
        {
            lease.ShouldNotBeNull();
        }

        var lostInfo = await lease.LeaseLostTask.WaitAsync(ct);
        lostInfo.Reason.ShouldBe(LeaseLostReason.Released);
        lostInfo.Exception.ShouldBeNull();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AcquireLeaseAsync_WithMultipleContenders_AllAcquireAndComplete(CancellationToken ct)
    {
        // We use real time for this test.
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
                    AcquireRetryDelay = TimeSpan.FromMilliseconds(100)
                };

                await using var lease = await leaseProvider.AcquireLeaseAsync(_resourceId, options, ct);

                lease.ShouldNotBeNull();

                // Simulate work
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, lease.LeaseLostToken);
                await Task.Delay(20, linkedCts.Token);
            });

        // If all contenders complete successfully before the test timeout, the test succeeds.
        await Task.WhenAll(contenders);
    }
}