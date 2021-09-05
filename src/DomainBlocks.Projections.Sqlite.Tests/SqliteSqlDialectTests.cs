using System.Data;
using DomainBlocks.Projections.Sql;
using NUnit.Framework;

namespace DomainBlocks.Projections.Sqlite.Tests
{
    [TestFixture]
    public class SqliteSqlDialectTests
    {
        public class CreateTable
        {
            [Test]
            public void ScriptIsBuilt()
            {
                var columns = new SqlColumnDefinitions()
                {
                    { "Col1", new SqlColumnDefinitionBuilder().Name("Col1").Type(DbType.String).Build()},
                    { "Col2", new SqlColumnDefinitionBuilder().Name("Col2").Type(DbType.String).Build()}
                };

                var dialect = new SqliteSqlDialect();
                var createTableScript = dialect.BuildCreateTableSql("Table1", columns.Values);

                Assert.That(createTableScript, Is.EqualTo(@"
CREATE TABLE IF NOT EXISTS Table1 (
Col1 TEXT NULL,
Col2 TEXT NULL
);
"));
            }

            [Test]
            public void PrimaryKeyColumnsAreInScript()
            {
                var columns = new SqlColumnDefinitions()
                {
                    { "Col1", new SqlColumnDefinitionBuilder().Name("Col1").Type(DbType.String).PrimaryKey().Build()},
                    { "Col2", new SqlColumnDefinitionBuilder().Name("Col2").Type(DbType.String).Build()}
                };

                var dialect = new SqliteSqlDialect();
                var createTableScript = dialect.BuildCreateTableSql("Table1", columns.Values);

                Assert.That(createTableScript, Is.EqualTo(@"
CREATE TABLE IF NOT EXISTS Table1 (
Col1 TEXT NOT NULL PRIMARY KEY,
Col2 TEXT NULL
);
"));
            }

            [Test]
            public void NullAndNotNullColumnsAreInScript()
            {
                var columns = new SqlColumnDefinitions()
                {
                    { "Col1", new SqlColumnDefinitionBuilder().Name("Col1").Type(DbType.String).NotNull().Build()},
                    { "Col2", new SqlColumnDefinitionBuilder().Name("Col2").Type(DbType.String).Null().Build()}
                };

                var dialect = new SqliteSqlDialect();
                var createTableScript = dialect.BuildCreateTableSql("Table1", columns.Values);

                Assert.That(createTableScript, Is.EqualTo(@"
CREATE TABLE IF NOT EXISTS Table1 (
Col1 TEXT NOT NULL,
Col2 TEXT NULL
);
"));
            }

            [TestCase(DbType.Double, "NUMERIC")]
            [TestCase(DbType.Single, "NUMERIC")]
            [TestCase(DbType.VarNumeric, "NUMERIC")]
            [TestCase(DbType.Binary, "INTEGER")]
            [TestCase(DbType.Boolean, "INTEGER")]
            [TestCase(DbType.Byte, "INTEGER")]
            [TestCase(DbType.Int16, "INTEGER")]
            [TestCase(DbType.Int32, "INTEGER")]
            [TestCase(DbType.Int64, "INTEGER")]
            [TestCase(DbType.SByte, "INTEGER")]
            [TestCase(DbType.UInt16, "INTEGER")]
            [TestCase(DbType.UInt32, "INTEGER")]
            [TestCase(DbType.UInt64, "INTEGER")]
            [TestCase(DbType.Object, "BLOB")]
            [TestCase(DbType.AnsiString, "TEXT")]
            [TestCase(DbType.AnsiStringFixedLength, "TEXT")]
            [TestCase(DbType.Currency, "TEXT")]
            [TestCase(DbType.Date, "TEXT")]
            [TestCase(DbType.DateTime, "TEXT")]
            [TestCase(DbType.DateTime2, "TEXT")]
            [TestCase(DbType.DateTimeOffset, "TEXT")]
            [TestCase(DbType.Decimal, "TEXT")]
            [TestCase(DbType.Guid, "TEXT")]
            [TestCase(DbType.String, "TEXT")]
            [TestCase(DbType.StringFixedLength, "TEXT")]
            [TestCase(DbType.Time, "TEXT")]
            [TestCase(DbType.Xml, "TEXT")]
            public void DataTypesAreMapped(DbType dbType, string columnType)
            {
                var columns = new SqlColumnDefinitions()
                {
                    { "Col1", new SqlColumnDefinitionBuilder().Name("Col1").Type(dbType).Build()},
                };

                var dialect = new SqliteSqlDialect();
                var createTableScript = dialect.BuildCreateTableSql("Table1", columns.Values);

                Assert.That(createTableScript, Is.EqualTo($@"
CREATE TABLE IF NOT EXISTS Table1 (
Col1 {columnType} NULL
);
"));
            }
        }

        public class Upsert
        {
            [Test]
            public void SingleColumn()
            {
                var columns = new SqlColumnDefinitions()
                {
                    { "Col1", new SqlColumnDefinitionBuilder().Name("Col1").Type(DbType.String).Build()},
                };

                var dialect = new SqliteSqlDialect();
                var upsertScript = dialect.BuildUpsertCommandText("Table1", columns);

                Assert.That(upsertScript, Is.EqualTo(@"
INSERT OR REPLACE INTO Table1 (
Col1
)
VALUES (
@Col1
);
"));
            }

            [Test]
            public void MultipleColumn()
            {
                var columns = new SqlColumnDefinitions()
                {
                    { "Col1", new SqlColumnDefinitionBuilder().Name("Col1").Type(DbType.String).Build()},
                    { "Col2", new SqlColumnDefinitionBuilder().Name("Col2").Type(DbType.String).Build()},
                };

                var dialect = new SqliteSqlDialect();
                var upsertScript = dialect.BuildUpsertCommandText("Table1", columns);

                Assert.That(upsertScript, Is.EqualTo(@"
INSERT OR REPLACE INTO Table1 (
Col1,
Col2
)
VALUES (
@Col1,
@Col2
);
"));
            }
        }

        public class Delete
        {
            [Test]
            public void SingleColumnPrimaryKey()
            {
                var columns = new SqlColumnDefinitions()
                {
                    { "Col1", new SqlColumnDefinitionBuilder().Name("Col1").Type(DbType.String).PrimaryKey().Build()},
                    { "Col2", new SqlColumnDefinitionBuilder().Name("Col2").Type(DbType.String).Build()},
                };

                var dialect = new SqliteSqlDialect();
                var deleteScript = dialect.BuildDeleteCommandText("Table1", columns);

                Assert.That(deleteScript, Is.EqualTo(@"
DELETE FROM Table1
WHERE
Col1 = @Col1
;
"));
            }

            [Test]
            public void MultipleColumnPrimaryKey()
            {
                var columns = new SqlColumnDefinitions()
                {
                    { "Col1", new SqlColumnDefinitionBuilder().Name("Col1").Type(DbType.String).PrimaryKey().Build()},
                    { "Col2", new SqlColumnDefinitionBuilder().Name("Col2").Type(DbType.String).PrimaryKey().Build()},
                    { "Col3", new SqlColumnDefinitionBuilder().Name("Col3").Type(DbType.String).Build()},
                };

                var dialect = new SqliteSqlDialect();
                var deleteScript = dialect.BuildDeleteCommandText("Table1", columns);

                Assert.That(deleteScript, Is.EqualTo(@"
DELETE FROM Table1
WHERE
Col1 = @Col1 AND
Col2 = @Col2
;
"));
            }
        }
    }
}
