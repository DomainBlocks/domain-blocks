using System.Net;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Unit;

public class MongoTransientErrorsTests
{
    private static readonly ConnectionId ConnectionId =
        new(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017)));

    [Test]
    public void IsTransient_NetworkError_ReturnsTrue()
    {
        var exception = new MongoConnectionException(ConnectionId, "Lost.", new IOException("Reset."));

        MongoTransientErrors.IsTransient(exception).ShouldBeTrue();
    }

    [Test]
    public void IsTransient_AuthenticationFailure_ReturnsFalse()
    {
        var exception = new MongoAuthenticationException(ConnectionId, "Bad credentials.");

        MongoTransientErrors.IsTransient(exception).ShouldBeFalse();
    }

    [Test]
    public void IsTransient_CursorNotFound_ReturnsTrue()
    {
        var exception = new MongoCursorNotFoundException(ConnectionId, 1, new BsonDocument("getMore", 1L));

        MongoTransientErrors.IsTransient(exception).ShouldBeTrue();
    }

    [Test]
    public void IsTransient_ServerSelectionTimeout_ReturnsTrue()
    {
        MongoTransientErrors.IsTransient(new TimeoutException("No primary.")).ShouldBeTrue();
    }

    [Test]
    public void IsTransient_NotPrimary_ReturnsTrue()
    {
        var exception = new MongoNotPrimaryException(ConnectionId, new BsonDocument("find", "x"), Result(10107));

        MongoTransientErrors.IsTransient(exception).ShouldBeTrue();
    }

    [TestCase(89)] // NetworkTimeout
    [TestCase(91)] // ShutdownInProgress
    [TestCase(134)] // ReadConcernMajorityNotAvailableYet
    public void IsTransient_CommandErrorThatReadsRetry_ReturnsTrue(int code)
    {
        MongoTransientErrors.IsTransient(CommandError(code)).ShouldBeTrue();
    }

    [TestCase(2)] // BadValue
    [TestCase(13)] // Unauthorized
    public void IsTransient_OtherCommandError_ReturnsFalse(int code)
    {
        MongoTransientErrors.IsTransient(CommandError(code)).ShouldBeFalse();
    }

    private static MongoCommandException CommandError(int code) =>
        new(ConnectionId, "Failed.", new BsonDocument("find", "x"), Result(code));

    private static BsonDocument Result(int code) => new() { { "ok", 0 }, { "code", code }, { "errmsg", "Failed." } };
}