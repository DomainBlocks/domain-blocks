using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class PostgresEventStoreOptionsTests
{
    private static readonly string[] InvalidSchemas = [string.Empty, "Dbx", "dbx-tests", new('a', 64)];

    private static readonly string[] BoundarySchemas = ["a", "_", new('a', 63)];

    [Test]
    public void Schema_Null_Throws()
    {
        var options = new PostgresEventStoreOptions();

        Should.Throw<ArgumentNullException>(() => options.Schema = null!);
    }

    [TestCaseSource(nameof(InvalidSchemas))]
    public void Schema_Invalid_Throws(string schema)
    {
        var options = new PostgresEventStoreOptions();

        Should.Throw<ArgumentException>(() => options.Schema = schema);
    }

    [TestCaseSource(nameof(BoundarySchemas))]
    public void Schema_Boundary_IsSet(string schema)
    {
        var options = new PostgresEventStoreOptions { Schema = schema };

        options.Schema.ShouldBe(schema);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void AppendQueueCapacity_NonPositive_Throws(int capacity)
    {
        var options = new PostgresEventStoreOptions();

        Should.Throw<ArgumentOutOfRangeException>(() => options.AppendQueueCapacity = capacity);
    }

    [TestCase(1)]
    [TestCase(int.MaxValue)]
    public void AppendQueueCapacity_Positive_IsSet(int capacity)
    {
        var options = new PostgresEventStoreOptions { AppendQueueCapacity = capacity };

        options.AppendQueueCapacity.ShouldBe(capacity);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void AppendMaxBatchSize_NonPositive_Throws(int size)
    {
        var options = new PostgresEventStoreOptions();

        Should.Throw<ArgumentOutOfRangeException>(() => options.AppendMaxBatchSize = size);
    }

    [TestCase(1)]
    [TestCase(int.MaxValue)]
    public void AppendMaxBatchSize_Positive_IsSet(int size)
    {
        var options = new PostgresEventStoreOptions { AppendMaxBatchSize = size };

        options.AppendMaxBatchSize.ShouldBe(size);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void ReadPageSize_NonPositive_Throws(int size)
    {
        var options = new PostgresEventStoreOptions();

        Should.Throw<ArgumentOutOfRangeException>(() => options.ReadPageSize = size);
    }

    [TestCase(1)]
    [TestCase(int.MaxValue)]
    public void ReadPageSize_Positive_IsSet(int size)
    {
        var options = new PostgresEventStoreOptions { ReadPageSize = size };

        options.ReadPageSize.ShouldBe(size);
    }

    [Test]
    public void Replication_Null_Throws()
    {
        var options = new PostgresEventStoreOptions();

        Should.Throw<ArgumentNullException>(() => options.Replication = null!);
    }
}