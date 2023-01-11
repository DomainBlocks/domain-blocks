using EventStore.Client;

namespace DomainBlocks.EventStore.Projections;

public class EventStorePersistentConnectionDescriptor
{
    public EventStorePersistentConnectionDescriptor(
        string stream,
        string groupName,
        int bufferSize,
        Core.UserCredentials userCredentials,
        TimeSpan? subscriptionStopTimeout = null,
        EventStorePersistentConnectionRetrySettings? retrySettings = null)
    {
        if (userCredentials == null) throw new ArgumentNullException(nameof(userCredentials));
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
        GroupName = groupName ?? throw new ArgumentNullException(nameof(groupName));
        BufferSize = bufferSize;
        SubscriptionStopTimeout = subscriptionStopTimeout ?? TimeSpan.FromSeconds(5);
        RetrySettings = retrySettings ?? EventStorePersistentConnectionRetrySettings.Default;
        UserCredentials = userCredentials.ToEsUserCredentials();
    }

    public string Stream { get; }
    public string GroupName { get; }
    public int BufferSize { get; }
    public TimeSpan SubscriptionStopTimeout { get; }
    public UserCredentials UserCredentials { get; }
    public EventStorePersistentConnectionRetrySettings RetrySettings { get; }
}