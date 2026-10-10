namespace DomainBlocks.Core;

/// <summary>
/// Provides methods for creating <see cref="Optional{T}"/> values.
/// </summary>
public static class Optional
{
    /// <summary>
    /// Creates an optional that contains the specified value.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value to contain.</param>
    /// <returns>An optional that contains <paramref name="value"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static Optional<T> From<T>(T value) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Optional<T>(value);
    }

    /// <summary>
    /// Creates an empty optional.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>An empty optional.</returns>
    public static Optional<T> None<T>() where T : notnull => default;
}

/// <summary>
/// Represents a value that may or may not be present.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
public readonly struct Optional<T> where T : notnull
{
    internal Optional(T value)
    {
        Value = value;
        HasValue = true;
    }

    /// <summary>
    /// Gets a value that indicates whether the optional contains a value.
    /// </summary>
    public bool HasValue { get; }

    /// <summary>
    /// Gets the contained value.
    /// </summary>
    /// <exception cref="InvalidOperationException">The optional is empty.</exception>
    public T Value => HasValue ? field : throw new InvalidOperationException("Optional has no value.");

    /// <summary>
    /// Converts a value to an optional that contains it.
    /// </summary>
    /// <param name="value">The value to contain.</param>
    /// <returns>An optional that contains <paramref name="value"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static implicit operator Optional<T>(T value) => Optional.From(value);

    /// <summary>
    /// Returns the string representation of the value, or "None" if the optional is empty.
    /// </summary>
    public override string? ToString() => HasValue ? Value.ToString() : "None";
}