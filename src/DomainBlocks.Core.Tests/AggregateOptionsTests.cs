using System;
using NUnit.Framework;

namespace DomainBlocks.Core.Tests;

[TestFixture]
public class AggregateOptionsTests
{
    [Test]
    public void CreateNewThrowsInvalidOperationWhenMissingFactory()
    {
        var options = new MutableAggregateOptions<object, object>();
        Assert.Throws<InvalidOperationException>(() => options.CreateNew());
    }

    [Test]
    public void MakeSnapshotKeyThrowsWhenMissingIdSelector()
    {
        var options = new MutableAggregateOptions<object, object>();
        Assert.Throws<InvalidOperationException>(() => options.MakeSnapshotKey(new object()));
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
}