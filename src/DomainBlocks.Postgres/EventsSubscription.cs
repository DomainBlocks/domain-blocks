using System.Runtime.CompilerServices;
using Npgsql.Replication;
using Npgsql.Replication.PgOutput;
using Npgsql.Replication.PgOutput.Messages;

namespace DomainBlocks.Postgres;

public class EventsSubscription : IEventsSubscription
{
    public async IAsyncEnumerable<object> Subscribe(
        EventsSubscriptionOptions options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var (connectionString, slotName, publicationName) = options;
        await using var conn = new LogicalReplicationConnection(connectionString);
        await conn.Open(ct);

        var slot = new PgOutputReplicationSlot(slotName);
        var replicationOptions = new PgOutputReplicationOptions(publicationName, 1);

        await foreach (var message in conn.StartReplication(slot, replicationOptions, ct))
        {
            if (message is InsertMessage insertMessage)
            {
                yield return await InsertMessageHandler.Handle(insertMessage, ct);
            }

            conn.SetReplicationStatus(message.WalEnd);
            await conn.SendStatusUpdate(ct);
        }
    }
}