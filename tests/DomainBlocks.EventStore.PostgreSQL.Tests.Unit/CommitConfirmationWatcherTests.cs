using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class CommitConfirmationWatcherTests
{
    [Test]
    public void IsUnconfirmedCommitWarning_WarningFromSyncRepWait_ReturnsTrue()
    {
        var notice = CreateNotice("WARNING", "SyncRepWaitForLSN");

        CommitConfirmationWatcher.IsUnconfirmedCommitWarning(notice).ShouldBeTrue();
    }

    [TestCase(null)]
    [TestCase("")]
    public void IsUnconfirmedCommitWarning_WarningWithoutRoutine_ReturnsTrue(string? routine)
    {
        var notice = CreateNotice("WARNING", routine);

        CommitConfirmationWatcher.IsUnconfirmedCommitWarning(notice).ShouldBeTrue();
    }

    [Test]
    public void IsUnconfirmedCommitWarning_WarningFromRaise_ReturnsFalse()
    {
        var notice = CreateNotice("WARNING", "exec_stmt_raise");

        CommitConfirmationWatcher.IsUnconfirmedCommitWarning(notice).ShouldBeFalse();
    }

    [Test]
    public void IsUnconfirmedCommitWarning_NoticeWithoutRoutine_ReturnsFalse()
    {
        var notice = CreateNotice("NOTICE", null);

        CommitConfirmationWatcher.IsUnconfirmedCommitWarning(notice).ShouldBeFalse();
    }

    private static PostgresNotice CreateNotice(string severity, string? routine) =>
        new("Message.", severity, severity, PostgresErrorCodes.Warning, routine: routine);
}