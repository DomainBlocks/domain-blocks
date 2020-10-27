using EventStore.ClientAPI;
using EventStore.ClientAPI.Embedded;
using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace Shopping.Infrastructure.Tests
{
    [SetUpFixture]
    public abstract class EmbeddedEventStoreTest : IDisposable
    {
        protected IEventStoreConnection EventStoreConnection { get; private set; }

        [OneTimeSetUp]
        public async Task SetUp()
        {
            var nodeBuilder = EmbeddedVNodeBuilder.AsSingleNode().RunInMemory().OnDefaultEndpoints();

            var node = nodeBuilder.Build();
            await node.StartAsync(true);

            var connection = EmbeddedEventStoreConnection.Create(node);
            await connection.ConnectAsync();

            EventStoreConnection = connection;
        }

        public void Dispose()
        {
            EventStoreConnection?.Dispose();
        }
    }
}