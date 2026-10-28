using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class PostgresTransientErrorsTests
{
    private static readonly Exception[] TransientErrors =
    [
        new ReplicationBarrierException("The replication barrier was not confirmed."),
        new ServerInRecoveryException("standby"),
        new NpgsqlException("Exception while reading from stream", new EndOfStreamException()),
        new NpgsqlException("Exception while reading from stream", new TimeoutException()),
        new PostgresException("Terminating connection.", "FATAL", "FATAL", PostgresErrorCodes.AdminShutdown),
        new PostgresException("Starting up.", "FATAL", "FATAL", PostgresErrorCodes.CannotConnectNow),
        new PostgresException("Too many clients.", "FATAL", "FATAL", PostgresErrorCodes.TooManyConnections)
    ];

    private static readonly Exception[] OtherErrors =
    [
        new PostgresException("Cannot assign TransactionIds.", "ERROR", "ERROR", PostgresErrorCodes.InternalError),
        new PostgresException("Relation does not exist.", "ERROR", "ERROR", PostgresErrorCodes.UndefinedTable),
        new PostgresException("Permission denied.", "ERROR", "ERROR", PostgresErrorCodes.InsufficientPrivilege),
        new NpgsqlException("Failed."),
        new IOException(),
        new InvalidOperationException()
    ];

    [TestCaseSource(nameof(TransientErrors))]
    public void IsTransient_TransientError_ReturnsTrue(Exception exception)
    {
        PostgresTransientErrors.IsTransient(exception).ShouldBeTrue();
    }

    [TestCaseSource(nameof(OtherErrors))]
    public void IsTransient_OtherError_ReturnsFalse(Exception exception)
    {
        PostgresTransientErrors.IsTransient(exception).ShouldBeFalse();
    }
}