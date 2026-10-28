namespace DomainBlocks.EventStore;

/// <summary>
/// Provides options for an append.
/// </summary>
public sealed class AppendOptions
{
    /// <summary>
    /// The default options.
    /// </summary>
    public static readonly AppendOptions Default = new();

    /// <summary>
    /// Gets how long to wait for the store to acknowledge the append before timing out, or
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> to wait without a timeout. The default is 30 seconds.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is zero, or negative and not <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>.
    /// </exception>
    public TimeSpan Timeout
    {
        get;
        init
        {
            if (value != System.Threading.Timeout.InfiniteTimeSpan)
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);

            field = value;
        }
    } = TimeSpan.FromSeconds(30);
}