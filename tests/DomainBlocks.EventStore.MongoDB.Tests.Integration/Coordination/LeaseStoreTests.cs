using DomainBlocks.EventStore.MongoDB.Coordination;
using DomainBlocks.EventStore.MongoDB.Schema;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration.Coordination;

[TestFixture]
public class LeaseStoreTests
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

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
    public async Task AcquireAsync_FirstAcquisition_ReturnsDocumentWithEpoch1()
    {
        var doc = await _store.AcquireAsync("test", LeaseDuration);

        doc.ShouldNotBeNull();
        doc.Epoch.ShouldBe(1);
        doc.HolderId.ShouldStartWith("test:");
        doc.CommitPosition.ShouldBeNull();
        doc.AcquiredAtUtc.ShouldBe(_timeProvider.GetUtcNow().UtcDateTime);
        doc.ExpiresAtUtc.ShouldBe(_timeProvider.GetUtcNow().UtcDateTime + LeaseDuration);
    }

    [Test]
    public async Task AcquireAsync_WhenLeaseHasOtherHolder_ReturnsNull()
    {
        await _store.AcquireAsync("holder1", LeaseDuration);

        var doc = await _store.AcquireAsync("holder2", LeaseDuration);

        doc.ShouldBeNull();
    }

    [Test]
    public async Task AcquireAsync_WhenExpired_TakesOverAndIncrementsEpoch()
    {
        var doc1 = await _store.AcquireAsync("holder1", LeaseDuration);

        _timeProvider.Advance(LeaseDuration + TimeSpan.FromMilliseconds(1));

        var doc2 = await _store.AcquireAsync("holder2", LeaseDuration);

        doc2.ShouldNotBeNull();
        doc2.Epoch.ShouldBe(doc1!.Epoch + 1);
        doc2.HolderId.ShouldNotBe(doc1.HolderId);
    }

    [Test]
    public async Task AcquireAsync_WhenExpired_InheritsCommitPosition()
    {
        var doc1 = await _store.AcquireAsync("holder1", LeaseDuration);
        await _store.TryAdvanceCommitPositionAsync(doc1!.HolderId, doc1.Epoch, 10);

        _timeProvider.Advance(LeaseDuration + TimeSpan.FromMilliseconds(1));

        var doc2 = await _store.AcquireAsync("holder2", LeaseDuration);

        doc2.ShouldNotBeNull();
        doc2.CommitPosition.ShouldBe(10);
    }

    [Test]
    public async Task TryRenewAsync_WhenHoldingLease_ReturnsTrue()
    {
        var doc = await _store.AcquireAsync("test", LeaseDuration);

        _timeProvider.Advance(TimeSpan.FromSeconds(1));

        var renewed = await _store.TryRenewAsync(doc!.HolderId, doc.Epoch, LeaseDuration);

        renewed.ShouldBeTrue();
    }

    [Test]
    public async Task TryRenewAsync_WhenLeaseExpired_ReturnsFalse()
    {
        var doc = await _store.AcquireAsync("test", LeaseDuration);

        _timeProvider.Advance(LeaseDuration + TimeSpan.FromMilliseconds(1));

        var renewed = await _store.TryRenewAsync(doc!.HolderId, doc.Epoch, LeaseDuration);

        renewed.ShouldBeFalse();
    }

    [Test]
    public async Task TryReleaseAsync_WhenHoldingLease_AllowsImmediateReacquisition()
    {
        var doc1 = await _store.AcquireAsync("holder1", LeaseDuration);
        await _store.TryReleaseAsync(doc1!.HolderId, doc1.Epoch);

        var doc2 = await _store.AcquireAsync("holder2", LeaseDuration);

        doc2.ShouldNotBeNull();
        doc2.Epoch.ShouldBe(doc1.Epoch + 1);
    }

    [Test]
    public async Task TryAdvanceCommitPositionAsync_WhenHoldingLease_SubsequentAdvancesAccumulate()
    {
        var doc = await _store.AcquireAsync("test", LeaseDuration);
        await _store.TryAdvanceCommitPositionAsync(doc!.HolderId, doc.Epoch, 5);
        await _store.TryAdvanceCommitPositionAsync(doc.HolderId, doc.Epoch, 3);

        _timeProvider.Advance(LeaseDuration + TimeSpan.FromMilliseconds(1));
        var doc2 = await _store.AcquireAsync("holder2", LeaseDuration);

        doc2!.CommitPosition.ShouldBe(8);
    }

    [Test]
    public async Task TryAdvanceCommitPositionAsync_WhenExpired_ReturnsFalse()
    {
        var doc1 = await _store.AcquireAsync("holder1", LeaseDuration);

        _timeProvider.Advance(LeaseDuration + TimeSpan.FromMilliseconds(1));

        var result = await _store.TryAdvanceCommitPositionAsync(doc1!.HolderId, doc1.Epoch, 5);

        result.ShouldBeFalse();
    }

    [Test]
    public async Task TryAdvanceCommitPositionAsync_WhenLeaseTakenOver_ReturnsFalse()
    {
        var doc1 = await _store.AcquireAsync("holder1", LeaseDuration);

        _timeProvider.Advance(LeaseDuration + TimeSpan.FromMilliseconds(1));
        await _store.AcquireAsync("holder2", LeaseDuration);

        var result = await _store.TryAdvanceCommitPositionAsync(doc1!.HolderId, doc1.Epoch, 5);

        result.ShouldBeFalse();
    }
}