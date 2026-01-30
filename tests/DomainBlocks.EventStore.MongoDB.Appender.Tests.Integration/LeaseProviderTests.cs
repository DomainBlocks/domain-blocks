using DomainBlocks.EventStore.MongoDB.Appender.Coordination;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Appender.Tests.Integration;

public class LeaseProviderTests
{
    private const int TestTimeoutMillis = 30 * 1000;

    private FakeTimeProvider _fakeTimeProvider = null!;
    private ILeaseProvider _leaseProvider = null!;
    private string _resourceId = null!;

    [SetUp]
    public void SetUp()
    {
        _fakeTimeProvider = new FakeTimeProvider();

        var mongoClient = new MongoClient(MongoConnectionStrings.Default);
        var db = mongoClient.GetDatabase("domainblocks");
        var leaseStates = db.GetCollection<LeaseState>("es_leases");
        var leaseStore = new LeaseStore(leaseStates, _fakeTimeProvider);

        using var loggerFactory = LoggerFactory.Create(x => x
            .AddConsole()
            .SetMinimumLevel(LogLevel.Debug));

        var logger = loggerFactory.CreateLogger<LeaseProvider>();

        _leaseProvider = new LeaseProvider(leaseStore, logger, _fakeTimeProvider);
        _resourceId = $"test_resource_{Guid.CreateVersion7():N}";
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task Should_acquire_lease(CancellationToken ct)
    {
        await using var lease = await _leaseProvider.AcquireLeaseAsync(_resourceId, cancellationToken: ct);

        lease.ShouldNotBeNull();
        lease.ResourceId.ShouldBe(_resourceId);
        lease.HolderId.ShouldStartWith(AcquireLeaseOptions.Default.HolderIdPrefix);
    }
}