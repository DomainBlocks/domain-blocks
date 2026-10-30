namespace DomainBlocks.EventStore;

public static class PositionExtensions
{
    extension<TSelf>(IPosition<TSelf> position) where TSelf : struct, IPosition<TSelf>
    {
        public long ToInt64() => checked((long)position.Value);
    }
}