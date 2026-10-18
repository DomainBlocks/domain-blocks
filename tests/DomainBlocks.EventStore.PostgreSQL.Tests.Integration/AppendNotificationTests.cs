using DomainBlocks.Testing.Integration.EventStore;
using NUnit.Framework;
using Shouldly;
using static DomainBlocks.EventStore.PostgreSQL.Tests.Integration.AppendFunctionClient;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// The notification that append_events sends on the schema's channel when it appends events. Each test shows that an
/// append sent no notification by the first notification being a later append's.
/// </summary>
[TestFixture]
public class AppendNotificationTests : PostgresIntegrationTest
{
    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Append_EventsAppended_NotifiesOnceWithLastPosition(CancellationToken ct)
    {
        await using var listener = await ListenAsync(ct);

        await Client.AppendAsync([Any("s1", JsonEvent(), JsonEvent()), Any("s2", JsonEvent())], ct);
        await Client.AppendAsync([Any("s3", JsonEvent())], ct);

        (await listener.NextAsync(ct)).ShouldBe(2);
        (await listener.NextAsync(ct)).ShouldBe(3);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Append_NothingAppended_DoesNotNotify(CancellationToken ct)
    {
        await Client.AppendAsync([Any("s1", JsonEvent())], ct);
        await using var listener = await ListenAsync(ct);

        await Client.AppendAsync([DoesNotExist("s1", JsonEvent())], ct);
        await Client.AppendAsync([Any("s2", JsonEvent())], ct);

        (await listener.NextAsync(ct)).ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Append_RolledBack_DoesNotNotify(CancellationToken ct)
    {
        await using var listener = await ListenAsync(ct);

        await using (var connection = await DataSource.OpenConnectionAsync(ct))
        await using (var transaction = await connection.BeginTransactionAsync(ct))
        {
            await Client.AppendAsync(connection, [Any("s1", JsonEvent())], ct);
            await transaction.RollbackAsync(ct);
        }

        await Client.AppendAsync([Any("s2", JsonEvent())], ct);

        // The rollback also undid the position it claimed, so the next append takes it.
        (await listener.NextAsync(ct)).ShouldBe(0);
    }

    private Task<AppendNotificationListener> ListenAsync(CancellationToken cancellationToken)
    {
        var channel = new SchemaObjectNames(Schema).AppendChannel;
        return AppendNotificationListener.StartAsync(DataSource, channel, cancellationToken);
    }
}