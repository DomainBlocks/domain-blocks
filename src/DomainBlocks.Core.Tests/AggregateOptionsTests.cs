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
        var options = new MutableAggregateOptions<MyAggregateWithId, object>();
        var aggregate = new MyAggregateWithId { Id = 123 };
        Assert.That(options.MakeSnapshotKey(aggregate), Is.EqualTo("myAggregateWithIdSnapshot-123"));
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

    // ReSharper disable once ClassNeverInstantiated.Local
    private class MyAggregate
    {
    }

    // ReSharper disable once ClassNeverInstantiated.Local
    private class MyAggregateWithId
    {
        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        public int Id { get; init; }
    }

    // ReSharper disable once ClassNeverInstantiated.Local
    private class MyAggregateWithNoPublicDefaultCtor
    {
        private MyAggregateWithNoPublicDefaultCtor()
        {
        }
    }
}