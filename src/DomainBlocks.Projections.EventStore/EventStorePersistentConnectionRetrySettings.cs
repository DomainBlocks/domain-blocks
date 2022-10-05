using System;

namespace DomainBlocks.Projections.EventStore;

public class EventStorePersistentConnectionRetrySettings
{
    public EventStorePersistentConnectionRetrySettings(int maxRetryCount, MaxRetriesFailureAction maxRetriesFailureAction, params TimeSpan[] retryDelays)
    {
        MaxRetryCount = maxRetryCount;
        RetryDelays = retryDelays;
        MaxRetriesFailureAction = maxRetriesFailureAction;
    }

    public static EventStorePersistentConnectionRetrySettings Default =
        new EventStorePersistentConnectionRetrySettings(3, 
            MaxRetriesFailureAction.Park, 
            TimeSpan.FromMilliseconds(500), 
            TimeSpan.FromSeconds(2), 
            TimeSpan.FromSeconds(10));

    public int MaxRetryCount { get; }
    public TimeSpan[] RetryDelays { get; }
    public MaxRetriesFailureAction MaxRetriesFailureAction { get; }

    public TimeSpan GetRetryDelay(int retryNumber)
    {
        if (retryNumber == 0 || RetryDelays.Length == 0)
        {
            return TimeSpan.Zero;
        }
            
        return retryNumber > RetryDelays.Length ? 
            RetryDelays[^1] : 
            RetryDelays[retryNumber - 1];
    }
}