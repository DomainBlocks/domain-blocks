using DomainBlocks.ThirdParty.SqlStreamStore.Streams;

namespace DomainBlocks.SqlStreamStore.Subscriptions;

public class SqlStreamStorePositionComparer : IComparer<long?>
{
    public int Compare(long? x, long? y)
    {
        // Compares two objects. An implementation of this method must return a
        // value less than zero if x is less than y, zero if x is equal to y, or a
        // value greater than zero if x is greater than y.
        
        
        // X and Y == null -> They are equal
        if (x == null && y == null)
        {
            return 0;
        }
        
        // x == null --> x is less than y. 
        if (x == null)
        {
            return -1;
        }

        // y == null --> y is less. 
        if (y == null)
        {
            return 1;
        }

        if (x.Value == Position.End)
        {
            return 1;
        }

        if (y.Value == Position.End)
        {
            return -1;
        }
        
        // values are not null, so compare them.
        return x.Value.CompareTo(y.Value);
    }
}