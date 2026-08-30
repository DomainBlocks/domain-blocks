namespace DomainBlocks.EventSourcing;

public readonly struct Optional<T> where T : notnull
{
    private Optional(T value)
    {
        Value = value;
        HasValue = true;
    }

    public bool HasValue { get; }

    public T Value => HasValue ? field : throw new InvalidOperationException("Optional has no value.");

    public static Optional<T> None => default;

    public static Optional<T> From(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Optional<T>(value);
    }

    public static implicit operator Optional<T>(T value) => From(value);
}