using DomainBlocks.EventStore.MongoDB.Client.Coordination;
using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Client.Tests.Integration.Coordination;

public class EventAppenderTests
{
    private MongoClient _mongoClient = null!;
    private ILoggerFactory _loggerFactory = null!;
    private IMongoCollection<AppendRequest> _appendRequests = null!;
    private IMongoCollection<EventLogEntry> _eventLog = null!;
    private EventAppender _eventAppender = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _mongoClient = new MongoClient(MongoConnectionStrings.Default);
        _loggerFactory = LoggerFactory.Create(x => x.AddConsole().SetMinimumLevel(LogLevel.Debug));

        var ns = EventStoreNamespaceSettings.Default;
        var db = _mongoClient.GetDatabase(ns.DatabaseName);

        _appendRequests = db.GetCollection<AppendRequest>(ns.AppendRequestsCollectionName);
        _eventLog = db.GetCollection<EventLogEntry>(ns.EventLogCollectionName);

        _eventAppender = new EventAppender(
            _eventLog,
            epoch: 2,
            initialCommitPosition: null,
            _loggerFactory.CreateLogger<EventAppender>());
    }

    [Test]
    public async Task Test()
    {
        var filter = Builders<AppendRequest>.Filter.Empty;
        var sort = Builders<AppendRequest>.Sort.Ascending(x => x.CreatedAtUtc);

        using var cursor = await _appendRequests
            .Find(filter, new FindOptions { BatchSize = 10 })
            .Sort(sort)
            .ToCursorAsync();

        while (await cursor.MoveNextAsync())
        {
            await _eventAppender.AppendEventsAsync(cursor.Current);
            break;
        }
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _mongoClient.Dispose();
        _loggerFactory.Dispose();
    }
}