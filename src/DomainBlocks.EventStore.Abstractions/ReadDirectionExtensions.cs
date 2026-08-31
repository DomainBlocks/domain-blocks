namespace DomainBlocks.EventStore.Abstractions;

public static class ReadDirectionExtensions
{
    extension(ReadDirection direction)
    {
        public bool ProducesEmptyReadFrom<TPos>(ReadOrigin<TPos> origin) where TPos : notnull => direction switch
        {
            ReadDirection.Forward => origin is ReadOrigin<TPos>.End,
            ReadDirection.Backward => origin is ReadOrigin<TPos>.Start,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown read direction.")
        };
    }
}