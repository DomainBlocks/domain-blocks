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
    public void MakeStreamKeyThrowWhenMissingIdToStreamKeySelector()
    {
        var options = new MutableAggregateOptions<object, object>();
        Assert.Throws<InvalidOperationException>(() => options.MakeStreamKey("id"));
    }
    
    [Test]
    public void MakeSnapshotKeyThrowWhenMissingIdToSnapshotKeySelector()
    {
        var options = new MutableAggregateOptions<object, object>();
        Assert.Throws<InvalidOperationException>(() => options.MakeSnapshotKey("id"));
    }
    
    [Test]
    public void MakeSnapshotKeyThrowWhenMissingIdSelector()
    {
        var options = new MutableAggregateOptions<object, object>();
        Assert.Throws<InvalidOperationException>(() => options.MakeSnapshotKey(new object()));
    }
}