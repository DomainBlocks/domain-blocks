using DomainBlocks.EventStore.MongoDB.Coordination;
using DomainBlocks.EventStore.MongoDB.Schema;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration.Coordination;

[TestFixture]
public class LeaseTests
{
    private const int TestTimeoutMillis = 30 * 1000;

    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RenewInterval = TimeSpan.FromSeconds(10);

    private IMongoCollection<LeaseDocument> _leases = null!;
    private FakeTimeProvider _timeProvider = null!;
    private LeaseStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        var db = new MongoClient(MongoConnectionStrings.Default).GetDatabase("domainblocks_tests");
        _leases = db.GetCollection<LeaseDocument>("test_leases");
        _timeProvider = new FakeTimeProvider();
        _store = new LeaseStore(_leases, _timeProvider);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _leases.Database.DropCollectionAsync("test_leases");
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AcquireAsync_WhenFirstAcquired_ReflectsDocumentEpochAndCommitPosition(CancellationToken ct)
    {
        await using var lease = await AcquireLeaseAsync(ct);

        lease.Epoch.ShouldBe(1);
        lease.CommitPosition.ShouldBe(-1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task LeaseLostToken_WhenLeaseIsAutoRenewed_RemainsUnset(CancellationToken ct)
    {
        await using var lease = await AcquireLeaseAsync(ct);

        // Advance past the original LeaseDuration in small steps (drives FakeTimeProvider.Delay).
        // If heartbeat fires at RenewInterval and renewal succeeds, expiry is pushed out.
        var target = LeaseDuration + TimeSpan.FromSeconds(1);
        var elapsed = TimeSpan.Zero;
        var step = TimeSpan.FromSeconds(1);

        while (elapsed < target && !lease.LeaseLostToken.IsCancellationRequested)
        {
            ct.ThrowIfCancellationRequested();
            _timeProvider.Advance(step);
            elapsed += step;
            await Task.Yield();
        }

        // If we passed the original duration without losing the lease, renewal extended it.
        lease.LeaseLostToken.IsCancellationRequested.ShouldBeFalse();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task LeaseLostToken_WhenRenewalFails_IsCancelled(CancellationToken ct)
    {
        // Set RenewInterval > LeaseDuration so the heartbeat fires only after the lease has expired.
        var doc = await _store.AcquireAsync("test", LeaseDuration, ct);
        doc.ShouldNotBeNull();

        await using var lease = new Lease(
            doc,
            _store,
            () => LeaseDuration,
            LeaseDuration * 2,
            NullLogger.Instance,
            _timeProvider);

        while (!lease.LeaseLostToken.IsCancellationRequested)
        {
            ct.ThrowIfCancellationRequested();
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task LeaseLostTask_WhenRenewalFails_CompletesWithRevokedReason(CancellationToken ct)
    {
        var doc = await _store.AcquireAsync("test", LeaseDuration, ct);
        doc.ShouldNotBeNull();

        await using var lease = new Lease(
            doc,
            _store,
            () => LeaseDuration,
            LeaseDuration * 2,
            NullLogger.Instance,
            _timeProvider);

        while (!lease.LeaseLostTask.IsCompleted)
        {
            ct.ThrowIfCancellationRequested();
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        var lostInfo = await lease.LeaseLostTask;
        lostInfo.Reason.ShouldBe(LeaseLostReason.Revoked);
        lostInfo.Exception.ShouldBeNull();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task LeaseLostTask_WhenDisposed_CompletesWithReleasedReason(CancellationToken ct)
    {
        var lease = await AcquireLeaseAsync(ct);

        await lease.DisposeAsync();

        var lostInfo = await lease.LeaseLostTask.WaitAsync(ct);
        lostInfo.Reason.ShouldBe(LeaseLostReason.Released);
        lostInfo.Exception.ShouldBeNull();
    }

    private async Task<Lease> AcquireLeaseAsync(CancellationToken ct = default)
    {
        var doc = await _store.AcquireAsync("test", LeaseDuration, ct);
        doc.ShouldNotBeNull();
        return new Lease(doc, _store, () => LeaseDuration, RenewInterval, NullLogger.Instance, _timeProvider);
    }
}