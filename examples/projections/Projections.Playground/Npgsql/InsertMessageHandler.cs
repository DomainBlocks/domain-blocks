// using System.Text.Json;
// using Npgsql.Replication.PgOutput.Messages;
// using Shopping.Domain.Events;
//
// namespace Projections.Playground.Npgsql;
//
// public static class InsertMessageHandler
// {
//     public static async Task<object> Handle(InsertMessage message, CancellationToken ct)
//     {
//         var columnNumber = 0;
//         var eventTypeName = string.Empty;
//
//         await foreach (var value in message.NewRow.WithCancellation(ct))
//         {
//             switch (columnNumber)
//             {
//                 case 5:
//                     eventTypeName = await value.GetTextReader().ReadToEndAsync();
//                     break;
//                 case 6 when value.GetDataTypeName().ToLower() == "jsonb":
//                 {
//                     var eventType = typeof(IDomainEvent).Assembly.GetType($"Shopping.Domain.Events.{eventTypeName}");
//                     if (eventType is null)
//                         throw new ArgumentOutOfRangeException(nameof(eventType));
//
//                     var @event =
//                         await JsonSerializer.DeserializeAsync(value.GetStream(), eventType, cancellationToken: ct);
//
//                     return @event!;
//                 }
//             }
//
//             columnNumber++;
//         }
//
//         throw new InvalidOperationException("You should not get here");
//     }
// }