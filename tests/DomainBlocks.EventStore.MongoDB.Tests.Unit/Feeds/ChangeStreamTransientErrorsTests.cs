using System.Net;
using DomainBlocks.EventStore.MongoDB.Feeds;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Unit.Feeds;

public class ChangeStreamTransientErrorsTests
{
    private static readonly Exception[] ResumableExceptions =
    [
        new TestMongoConnectionException(isNetworkException: true),
        new MongoConnectionPoolPausedException(null),
        new MongoCursorNotFoundException(CreateConnectionId(), 0, []),
        new TimeoutException(),
        CreateResumableMongoException()
    ];

    private static readonly Exception[] UnresumableExceptions =
    [
        new TestMongoConnectionException(isNetworkException: false),
        new InvalidOperationException()
    ];

    [TestCaseSource(nameof(ResumableExceptions))]
    public void IsTransient_ResumableError_ReturnsTrue(Exception exception)
    {
        ChangeStreamTransientErrors.IsTransient(exception).ShouldBeTrue();
    }

    [TestCaseSource(nameof(UnresumableExceptions))]
    public void IsTransient_UnresumableError_ReturnsFalse(Exception exception)
    {
        ChangeStreamTransientErrors.IsTransient(exception).ShouldBeFalse();
    }

    [Test]
    public void IsTransient_HistoryLost_ReturnsTrue()
    {
        var exception = new MongoCommandException(
            CreateConnectionId(),
            "The command failed.",
            new BsonDocument("aggregate", 1),
            new BsonDocument { { "ok", 0 }, { "code", MongoErrorCodes.ChangeStreamHistoryLost } });

        ChangeStreamTransientErrors.IsTransient(exception).ShouldBeTrue();
    }

    private static MongoException CreateResumableMongoException()
    {
        var exception = new MongoException("Resumable error");
        exception.AddErrorLabel(MongoErrorLabels.ResumableChangeStreamError);
        return exception;
    }

    private static ConnectionId CreateConnectionId()
    {
        var clusterId = new ClusterId();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 27017);
        var serverId = new ServerId(clusterId, endpoint);
        return new ConnectionId(serverId);
    }

    private class TestMongoConnectionException(bool isNetworkException) :
        MongoConnectionException(CreateConnectionId(), null)
    {
        public override bool IsNetworkException => isNetworkException;
    }
}