// using DomainBlocks.EventStore.Abstractions;
// using DomainBlocks.EventStore.Abstractions.Events;
// using DomainBlocks.Testing.Integration;
// using NUnit.Framework;
//
// namespace DomainBlocks.EventStore.KurrentDB.Tests.Integration;
//
// [TestFixture]
// public class KurrentDBEventStoreConnectionTests : EventStoreClientTests<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>
// {
//     protected override Task<IEventStoreConnectionProvider<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>>
//         GetConnectionProviderAsync()
//     {
//         const string connectionString = "kurrentdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false";
//         var provider = KurrentDBEventStoreConnectionProvider.FromConnectionString(connectionString);
//         return Task.FromResult<IEventStoreConnectionProvider<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>>(provider);
//     }
//
//     protected override AppendEvent<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> CreateTestEvent(string eventName)
//     {
//         return TestEventsHelper.CreateTestEvent(eventName);
//     }
// }