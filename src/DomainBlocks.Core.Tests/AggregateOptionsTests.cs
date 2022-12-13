using System;
using NUnit.Framework;

namespace DomainBlocks.Core.Tests;

[TestFixture]
public class AggregateOptionsTests
{
    [Test]
    public void CreateNewUsesDefaultConstructorWhenNoFactorySpecified()
    {
        var options = new MutableAggregateOptions<MyAggregate, object>();
        var aggregate = options.CreateNew();
        Assert.That(aggregate, Is.Not.Null);
    }

    [Test]
    public void CreateNewThrowsInvalidOperationWhenNoDefaultConstructorAndMissingFactory()
    {
        var options = new MutableAggregateOptions<MyAggregateWithNoPublicDefaultCtor, object>();
        Assert.Throws<InvalidOperationException>(() => options.CreateNew());
    }

    [Test]
    public void MakeSnapshotKeyUsesDefaultIdSelectorWhenIdPropertyExists()
    {
        var options1 = new MutableAggregateOptions<MyAggregateWithId1, object>();
        var options2 = new MutableAggregateOptions<MyAggregateWithId2, object>();
        var aggregate1 = new MyAggregateWithId1 { Id = 123 };
        var aggregate2 = new MyAggregateWithId2 { MyAggregateWithId2Id = 456 };
        Assert.That(options1.MakeSnapshotKey(aggregate1), Is.EqualTo("myAggregateWithId1Snapshot-123"));
        Assert.That(options2.MakeSnapshotKey(aggregate2), Is.EqualTo("myAggregateWithId2Snapshot-456"));
    }

    [Test]
    public void MakeSnapshotKeyThrowsWhenMissingIdSelectorAndNoIdPropertyExists()
    {
        var options = new MutableAggregateOptions<MyAggregate, object>();
        Assert.Throws<InvalidOperationException>(() => options.MakeSnapshotKey(new MyAggregate()));
    }

    [Test]
    public void MakeStreamKeyUsesDefaultSelectorWhenNotSpecified()
    {
        var options = new MutableAggregateOptions<MyAggregate, object>();
        var streamKey = options.MakeStreamKey("1");
        Assert.That(streamKey, Is.EqualTo("myAggregate-1"));
    }

    [Test]
    public void MakeSnapshotKeyUsesDefaultSelectorWhenNotSpecified()
    {
        var options = new MutableAggregateOptions<MyAggregate, object>();
        var snapshotKey = options.MakeSnapshotKey("1");
        Assert.That(snapshotKey, Is.EqualTo("myAggregateSnapshot-1"));
    }

    [Test]
    public void KeySelectorsUsesPrefixWhenSpecified()
    {
        var options = new MutableAggregateOptions<MyAggregate, object>().WithKeyPrefix("myPrefix");
        Assert.That(options.MakeStreamKey("1"), Is.EqualTo("myPrefix-1"));
        Assert.That(options.MakeSnapshotKey("1"), Is.EqualTo("myPrefixSnapshot-1"));
    }

    private class MyAggregate
    {
    }

    // ReSharper disable UnusedAutoPropertyAccessor.Local
    private class MyAggregateWithId1
    {
        public int Id { get; init; }
    }

    private class MyAggregateWithId2
    {
        public int MyAggregateWithId2Id { get; init; }
    }
    // ReSharper restore UnusedAutoPropertyAccessor.Local

    // ReSharper disable once ClassNeverInstantiated.Local
    private class MyAggregateWithNoPublicDefaultCtor
    {
        private MyAggregateWithNoPublicDefaultCtor()
        {
        }
    }
}