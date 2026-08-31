namespace DomainBlocks.EventSourcing;

/// <summary>
/// Provides factory methods for optional values.
/// </summary>
public static class Optional
{
    /// <summary>
    /// Creates an optional containing the specified value.
    /// </summary>
    /// <typeparam name="T">The type of the optional value.</typeparam>
    /// <param name="value">The value to contain.</param>
    /// <returns>An optional containing <paramref name="value"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static Optional<T> From<T>(T value) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Optional<T>(value);
    }

    /// <summary>
    /// Creates an empty optional with no value.
    /// </summary>
    /// <typeparam name="T">The type of the optional value.</typeparam>
    /// <returns>An empty optional.</returns>
    public static Optional<T> None<T>() where T : notnull => default;
}

/// <summary>
/// Represents a value that may or may not be present.
/// </summary>
/// <typeparam name="T">The type of the optional value.</typeparam>
public readonly struct Optional<T> where T : notnull
{
    internal Optional(T value)
    {
        Value = value;
        HasValue = true;
    }

    /// <summary>
    /// Gets a value indicating whether an optional value is present.
    /// </summary>
    public bool HasValue { get; }

    /// <summary>
    /// Gets the contained value.
    /// </summary>
    /// <exception cref="InvalidOperationException">The optional has no value.</exception>
    public T Value => HasValue ? field : throw new InvalidOperationException("Optional has no value.");

    /// <summary>
    /// Converts a value to an optional containing that value.
    /// </summary>
    /// <param name="value">The value to contain.</param>
    /// <returns>An optional containing <paramref name="value"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static implicit operator Optional<T>(T value) => Optional.From(value);
}