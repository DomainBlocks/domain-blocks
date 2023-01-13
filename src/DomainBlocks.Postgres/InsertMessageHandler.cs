using System.Text;
using Npgsql.Replication.PgOutput.Messages;

namespace DomainBlocks.Postgres;

public static class InsertMessageHandler
{
    public static async Task<(string, string)> Handle(InsertMessage message, CancellationToken ct)
    {
        var columnNumber = 0;
        var eventTypeName = string.Empty;

        await foreach (var value in message.NewRow.WithCancellation(ct))
        {
            switch (columnNumber)
            {
                case 5:
                    eventTypeName = await value.GetTextReader().ReadToEndAsync();
                    break;
                case 6 when value.GetDataTypeName().ToLower() == "jsonb":
                {
                    await using var stream = value.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    var payload = await reader.ReadToEndAsync();
                    return (eventTypeName, payload);
                }
            }

            columnNumber++;
        }

        throw new InvalidOperationException("You should not get here");
    }
}