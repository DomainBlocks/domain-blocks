using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Unit;

public class MongoEventStoreOptionsTests
{
    private static readonly string[] InvalidDatabaseNames = [string.Empty, " ", "dbx.tests", "dbx\0tests"];

    private static readonly string[] InvalidCollectionNames = [string.Empty, " ", "dbx\0tests"];

    [Test]
    public void DatabaseName_Null_Throws()
    {
        var options = new MongoEventStoreOptions();

        Should.Throw<ArgumentNullException>(() => options.DatabaseName = null!);
    }

    [TestCaseSource(nameof(InvalidDatabaseNames))]
    public void DatabaseName_Invalid_Throws(string name)
    {
        var options = new MongoEventStoreOptions();

        Should.Throw<ArgumentException>(() => options.DatabaseName = name);
    }

    [TestCase("a")]
    [TestCase("dbx_tests")]
    public void DatabaseName_Valid_IsSet(string name)
    {
        var options = new MongoEventStoreOptions { DatabaseName = name };

        options.DatabaseName.ShouldBe(name);
    }

    [Test]
    public void EventLogCollectionName_Null_Throws()
    {
        var options = new MongoEventStoreOptions();

        Should.Throw<ArgumentNullException>(() => options.EventLogCollectionName = null!);
    }

    [TestCaseSource(nameof(InvalidCollectionNames))]
    public void EventLogCollectionName_Invalid_Throws(string name)
    {
        var options = new MongoEventStoreOptions();

        Should.Throw<ArgumentException>(() => options.EventLogCollectionName = name);
    }

    [TestCase("a")]
    [TestCase("dbx.event_log")]
    public void EventLogCollectionName_Valid_IsSet(string name)
    {
        var options = new MongoEventStoreOptions { EventLogCollectionName = name };

        options.EventLogCollectionName.ShouldBe(name);
    }

    [Test]
    public void SequencesCollectionName_Null_Throws()
    {
        var options = new MongoEventStoreOptions();

        Should.Throw<ArgumentNullException>(() => options.SequencesCollectionName = null!);
    }

    [TestCaseSource(nameof(InvalidCollectionNames))]
    public void SequencesCollectionName_Invalid_Throws(string name)
    {
        var options = new MongoEventStoreOptions();

        Should.Throw<ArgumentException>(() => options.SequencesCollectionName = name);
    }

    [TestCase("a")]
    [TestCase("dbx.sequences")]
    public void SequencesCollectionName_Valid_IsSet(string name)
    {
        var options = new MongoEventStoreOptions { SequencesCollectionName = name };

        options.SequencesCollectionName.ShouldBe(name);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void AppendQueueCapacity_NonPositive_Throws(int capacity)
    {
        var options = new MongoEventStoreOptions();

        Should.Throw<ArgumentOutOfRangeException>(() => options.AppendQueueCapacity = capacity);
    }

    [TestCase(1)]
    [TestCase(int.MaxValue)]
    public void AppendQueueCapacity_Positive_IsSet(int capacity)
    {
        var options = new MongoEventStoreOptions { AppendQueueCapacity = capacity };

        options.AppendQueueCapacity.ShouldBe(capacity);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void AppendMaxBatchSize_NonPositive_Throws(int size)
    {
        var options = new MongoEventStoreOptions();

        Should.Throw<ArgumentOutOfRangeException>(() => options.AppendMaxBatchSize = size);
    }

    [TestCase(1)]
    [TestCase(int.MaxValue)]
    public void AppendMaxBatchSize_Positive_IsSet(int size)
    {
        var options = new MongoEventStoreOptions { AppendMaxBatchSize = size };

        options.AppendMaxBatchSize.ShouldBe(size);
    }
}