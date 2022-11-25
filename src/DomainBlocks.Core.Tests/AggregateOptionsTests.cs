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
    public void MakeSnapshotKeyThrowsWhenMissingIdSelector()
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

    // ReSharper disable once ClassNeverInstantiated.Local
    private class MyAggregate
    {
    }

    // ReSharper disable once ClassNeverInstantiated.Local
    private class MyAggregateWithNoPublicDefaultCtor
    {
        private MyAggregateWithNoPublicDefaultCtor()
        {
        }
    }
}