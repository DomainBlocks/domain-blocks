namespace DomainBlocks.EventStore;

public static class ReadDirectionExtensions
{
    extension(ReadDirection direction)
    {
        public bool ProducesEmptyReadFrom<TPos>(ReadOrigin<TPos> origin) where TPos : notnull => direction switch
        {
            ReadDirection.Forward => origin is SequenceEnd,
            ReadDirection.Backward => origin is SequenceStart,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown read direction.")
        };
    }
}