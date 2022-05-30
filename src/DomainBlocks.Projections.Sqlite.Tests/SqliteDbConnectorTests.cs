using DomainBlocks.Projections.Sql;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace DomainBlocks.Projections.Sqlite.Tests
{
    [TestFixture]
    public class SqliteDbConnectorTests
    {
        [Test]
        public void ParametersAreBound()
        {
            var command = new SqliteConnection().CreateCommand();
            var @event = new TestEvent(1, 2);
            var columns = new SqlColumnDefinitions()
            {
                { "Col1", new SqlColumnDefinitionBuilder().Name("Col1").Build()},
                { "Col2", new SqlColumnDefinitionBuilder().Name("Col2").Build()}
            };

            var parameterBindingMap =
                new ParameterBindingMap<TestEvent>(new Dictionary<string, Func<TestEvent, object>>
                {
                    { "Col1", e => e.Id },
                    { "Col2", e => e.Value }
                });

            var dbConnector = new SqliteDbConnector("Data Source=:memory:");

            dbConnector.BindParameters(command, @event, columns, parameterBindingMap);

            Assert.That(command.Parameters.Count, Is.EqualTo(2));

            var parameter1 = command.Parameters[0];

            Assert.That(parameter1.ParameterName, Is.EqualTo("@Col1"));
            Assert.That(parameter1.SqliteType, Is.EqualTo(SqliteType.Integer));
            Assert.That(parameter1.Value, Is.EqualTo(1));

            var parameter2 = command.Parameters[1];

            Assert.That(parameter2.ParameterName, Is.EqualTo("@Col2"));
            Assert.That(parameter2.SqliteType, Is.EqualTo(SqliteType.Integer));
            Assert.That(parameter2.Value, Is.EqualTo(2));
        }

        private class TestEvent
        {
            public const string Name = nameof(TestEvent);

            public TestEvent(int id, int value)
            {
                Id = id;
                Value = value;
            }

            public int Id { get; }
            public int Value { get; }
        }
    }
}