using DomainBlocks.SqlStreamStore.Subscriptions;
using DomainBlocks.ThirdParty.SqlStreamStore.Streams;
using NUnit.Framework;

namespace DomainBlocks.SqlStreamStore.Tests.PositionComparer;

[TestFixture]
public class SqlStreamStorePositionComparerTests
{
    [Test]
    public void PositionComparer_ConsidersNull_AsMinPosition()
    {
        var comparer = new SqlStreamStorePositionComparer();
        Assert.That(comparer.Compare(null, 1), Is.EqualTo(-1));
        Assert.That(comparer.Compare(null, 0), Is.EqualTo(-1));
        Assert.That(comparer.Compare(1, null), Is.EqualTo(1));
        Assert.That(comparer.Compare(0, null), Is.EqualTo(1));
    }

    [Test]
    public void PositionComparer_PositionEnd_IsLargestPosition()
    {
        var comparer = new SqlStreamStorePositionComparer();
        Assert.That(comparer.Compare(Position.End, null), Is.EqualTo(1));
        Assert.That(comparer.Compare(null, Position.End), Is.EqualTo(-1));
        Assert.That(comparer.Compare(9999, Position.End), Is.EqualTo(-1));
        Assert.That(comparer.Compare(Position.End, 9999), Is.EqualTo(1));
    }
    
    [Test]
    public void PositionComparer_Equality_ForValues()
    {
        var comparer = new SqlStreamStorePositionComparer();
        Assert.That(comparer.Compare(1, 1), Is.EqualTo(0));
        Assert.That(comparer.Compare(9999, 9999), Is.EqualTo(0));
        Assert.That(comparer.Compare(null, null), Is.EqualTo(0));
    }

    [Test]
    public void ArrayOfPositionsWithNulls_Returns_CorrectMinimumPosition()
    {
        var list = new long?[] {null, 1, 2, 3, 4, 5, -1};
        var comparer = new SqlStreamStorePositionComparer();
        
        var result = list.Aggregate((acc, next) =>
        {
            var comparisonResult = comparer.Compare(acc, next);
            if (comparisonResult < 0) return acc;
            return comparisonResult > 0 ? next : acc;
        });
        
        Assert.That(result, Is.EqualTo(null));
    }
}