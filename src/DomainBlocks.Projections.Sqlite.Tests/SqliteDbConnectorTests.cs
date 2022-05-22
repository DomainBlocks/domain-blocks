using DomainBlocks.Projections.Sql;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Data;
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
                { "Col1", new SqlColumnDefinitionBuilder().Name("Col1").Type(DbType.Int32).Build()},
                { "Col2", new SqlColumnDefinitionBuilder().Name("Col2").Type(DbType.Int32).Build()}
            };

            var parameterBindingMap =
                new ParameterBindingMap<TestEvent>(new Dictionary<string, Func<TestEvent, object>>
                {
                    {"Col1", e => e.Id},
                    {"Col2", e => e.Value }
                });

            var dbConnector = new SqliteDbConnector("not important");

            dbConnector.BindParameters(command, @event, columns, parameterBindingMap);

            Assert.That(command.Parameters.Count, Is.EqualTo(2));

            var parameter1 = command.Parameters[0];

            Assert.That(parameter1.ParameterName, Is.EqualTo("@Col1"));
            Assert.That(parameter1.DbType, Is.EqualTo(columns["Col1"].DataType));
            Assert.That(parameter1.Value, Is.EqualTo(1));

            var parameter2 = command.Parameters[1];

            Assert.That(parameter2.ParameterName, Is.EqualTo("@Col2"));
            Assert.That(parameter2.DbType, Is.EqualTo(columns["Col2"].DataType));
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