using System.Data;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DomainBlocks.Projections.Sql.Tests
{
    [TestFixture]
    public class SqlTransactionTests
    {
        [Test]
        public async Task StartingDispatcherOpensConnection()
        {
            var scenario = new SqlProjectionScenario();

            Assert.That(scenario.DbConnector.Connection.State, Is.EqualTo(ConnectionState.Closed));

            await scenario.Dispatcher.StartAsync();

            Assert.That(scenario.DbConnector.Connection.State, Is.EqualTo(ConnectionState.Open));
        }

        [Test]
        public async Task TransactionIsUsedForEventsPublishedBeforeCaughtUpNotification()
        {
            var scenario = new SqlProjectionScenario();

            await scenario.Dispatcher.StartAsync();

            await scenario.Publisher.SendEvent(new TestEvent(1, 1), TestEvent.Name);
            await scenario.Publisher.SendEvent(new TestEvent(2, 1), TestEvent.Name);

            var transaction = scenario.DbConnector.Connection.ActiveTransaction;
            Assert.That(transaction, Is.Not.Null);
            Assert.That(transaction.HasBeenCommitted, Is.False);

            await scenario.Publisher.SendCaughtUp();

            Assert.That(transaction.HasBeenCommitted, Is.True);
        }

        [Test]
        public async Task TransactionIsUsedWhenHandlingEventAfterCaughtUp()
        {
            var scenario = new SqlProjectionScenario();

            await scenario.Dispatcher.StartAsync();
            await scenario.Publisher.SendCaughtUp();
            scenario.DbConnector.Connection.ActiveTransaction = null;

            await scenario.Publisher.SendEvent(new TestEvent(1, 1), TestEvent.Name);

            var transaction = scenario.DbConnector.Connection.ActiveTransaction;
            Assert.That(transaction, Is.Not.Null);
            Assert.That(transaction.HasBeenCommitted, Is.True);
        }

        [Test]
        public async Task EachEventIsHandledIsItsOwnTransaction()
        {
            var scenario = new SqlProjectionScenario();
            await scenario.Dispatcher.StartAsync();
            await scenario.Publisher.SendCaughtUp();

            scenario.DbConnector.Connection.TransactionsBegun.Clear();

            await scenario.Publisher.SendEvent(new TestEvent(1, 1), TestEvent.Name);
            await scenario.Publisher.SendEvent(new TestEvent(1, 2), TestEvent.Name);

            Assert.That(scenario.DbConnector.Connection.TransactionsBegun.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task TransactionIsNotUsedBeforeCaughtUpWhenTurnedOffInSettings()
        {
            var contextSettings = SqlContextSettings.Default with { UseTransactionBeforeCaughtUp = false};

            var scenario = new SqlProjectionScenario(sqlContextSettings: contextSettings);
            await scenario.Dispatcher.StartAsync();

            await scenario.Publisher.SendEvent(new TestEvent(1, 1), TestEvent.Name);
            await scenario.Publisher.SendEvent(new TestEvent(2, 1), TestEvent.Name);

            Assert.That(scenario.DbConnector.Connection.ActiveTransaction, Is.Null);

            await scenario.Publisher.SendCaughtUp();

            Assert.That(scenario.DbConnector.Connection.ActiveTransaction, Is.Null);
        }

        [Test]
        public async Task EventsAreNotHandledInTransactionWhenTurnedOffInSettings()
        {
            var contextSettings = SqlContextSettings.Default with { HandleLiveEventsInTransaction = false };

            var scenario = new SqlProjectionScenario(sqlContextSettings: contextSettings);
            await scenario.Dispatcher.StartAsync();
            await scenario.Publisher.SendCaughtUp();

            // Transaction should still have been used for before the caught up part
            Assert.That(scenario.DbConnector.Connection.ActiveTransaction, Is.Not.Null);
            scenario.DbConnector.Connection.ActiveTransaction = null;

            await scenario.Publisher.SendEvent(new TestEvent(1, 1), TestEvent.Name);

            Assert.That(scenario.DbConnector.Connection.ActiveTransaction, Is.Null);
        }
    }
}
