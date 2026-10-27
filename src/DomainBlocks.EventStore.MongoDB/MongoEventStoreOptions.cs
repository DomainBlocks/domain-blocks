using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// Provides options for a MongoDB event store.
/// </summary>
public sealed class MongoEventStoreOptions
{
    /// <summary>
    /// Gets or sets the name of the database that holds the event log and sequence collections. The default is
    /// <c>domainblocks</c>.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The value is empty or only white space, or contains a period or the null character.
    /// </exception>
    public string DatabaseName
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (!DatabaseNamespace.IsValid(value))
            {
                throw new ArgumentException(
                    $"Database name '{value}' is invalid. It must not be empty or only white space, or " +
                    "contain a period or the null character.",
                    nameof(value));
            }

            field = value;
        }
    } = "domainblocks";

    /// <summary>
    /// Gets or sets the name of the collection that holds the event log. The default is <c>dbx_event_log</c>.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The value is empty or only white space, or contains the null character.
    /// </exception>
    public string EventLogCollectionName
    {
        get;
        set
        {
            ThrowIfInvalidCollectionName(value);
            field = value;
        }
    } = "dbx_event_log";

    /// <summary>
    /// Gets or sets the name of the collection that holds the sequence document from which log positions are claimed.
    /// The default is <c>dbx_sequences</c>.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The value is empty or only white space, or contains the null character.
    /// </exception>
    public string SequencesCollectionName
    {
        get;
        set
        {
            ThrowIfInvalidCollectionName(value);
            field = value;
        }
    } = "dbx_sequences";

    /// <summary>
    /// Gets or sets the maximum number of queued append requests before callers wait. The default is 1,000.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    public int AppendQueueCapacity
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 1_000;

    /// <summary>
    /// Gets or sets the maximum number of append requests committed in one transaction. The default is 500.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    public int AppendMaxBatchSize
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 500;

    private static void ThrowIfInvalidCollectionName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!CollectionNamespace.IsValid(value))
        {
            throw new ArgumentException(
                $"Collection name '{value}' is invalid. It must not be empty or only white space, or " +
                "contain the null character.",
                nameof(value));
        }
    }
}