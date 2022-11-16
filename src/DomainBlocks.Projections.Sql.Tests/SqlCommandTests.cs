using System;
using System.Threading.Tasks;
using DomainBlocks.Projections.Sql.Tests.Fakes;
using NUnit.Framework;

namespace DomainBlocks.Projections.Sql.Tests
{
    [TestFixture]
    public class SqlCommandTests
    {
        [Test]
        public async Task CreateTableScriptIsExecutedAfterStartingDispatcher()
        {
            var scenario = new SqlProjectionScenario();
            var commands = scenario.DbConnector.Connection.ExecutedCommands;

            Assert.That(commands.Count, Is.EqualTo(0));

            await scenario.Dispatcher.StartAsync();

            Assert.That(commands.Count, Is.EqualTo(1));
            Assert.That(commands[0].CommandText.Contains("CREATE TABLE", StringComparison.InvariantCultureIgnoreCase));
        }

        [Test]
        public async Task CommandsAreCombinedWhenEventProjectsToUpsertAndCustomSql()
        {
            var scenario = new SqlProjectionScenario();
            await scenario.Dispatcher.StartAsync();
            // Executed commands contains create table sql after starting dispatcher
            scenario.DbConnector.Connection.ExecutedCommands.Clear();

            await scenario.Publisher.SendEvent(new UpsertCustomSqlEvent(1, 1), UpsertCustomSqlEvent.Name);

            var commands = scenario.DbConnector.Connection.ExecutedCommands;
            Assert.That(commands.Count, Is.EqualTo(1));
            Assert.That(commands[0].CommandText.Contains(UpsertCustomSqlEvent.CustomSqlText));
        }

        [Test]
        public async Task CommandsAreCombinedWhenEventProjectsToDeleteAndCustomSql()
        {
            var scenario = new SqlProjectionScenario();
            await scenario.Dispatcher.StartAsync();
            // Executed commands contains create table sql after starting dispatcher
            scenario.DbConnector.Connection.ExecutedCommands.Clear();

            await scenario.Publisher.SendEvent(new DeleteCustomSqlEvent(1, 1), DeleteCustomSqlEvent.Name);

            var commands = scenario.DbConnector.Connection.ExecutedCommands;
            Assert.That(commands.Count, Is.EqualTo(1));
            Assert.That(commands[0].CommandText.Contains(UpsertCustomSqlEvent.CustomSqlText));
        }

        [Test]
        public void CannotHaveProjectionThatUpsertsAndDeletes()
        {
            var registryBuilder = new ProjectionRegistryBuilder();
            var projection = new FakeSqlProjection(SqlContextSettings.Default);

            registryBuilder.Event<UpsertDeleteEvent>()
                           .FromName(UpsertDeleteEvent.Name)
                           .ToSqlProjection(projection)
                           .ParameterMappings(("Col1", e => e.Id),
                                              ("Col2", e => e.Value))
                           .ExecutesUpsert()
                           .ExecutesDelete();

            var exception = Assert.Throws<InvalidOperationException>(() => registryBuilder.Build());

            Assert.That(exception.Message, Is.EqualTo("An event cannot perform both an upsert and a delete on the same projection."));
        }
    }
}