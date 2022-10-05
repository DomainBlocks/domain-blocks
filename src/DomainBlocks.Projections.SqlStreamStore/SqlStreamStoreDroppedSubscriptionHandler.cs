using System;
using System.Threading.Tasks;
using DomainBlocks.Common;
using Microsoft.Extensions.Logging;
using SqlStreamStore.Subscriptions;

namespace DomainBlocks.Projections.SqlStreamStore;

public class SqlStreamStoreDroppedSubscriptionHandler
{
    private readonly Action _stop;
    private readonly Func<Task> _resubscribe;
    private static readonly ILogger<SqlStreamStoreDroppedSubscriptionHandler> Log = Logger.CreateFor<SqlStreamStoreDroppedSubscriptionHandler>();

    private int _maxSubscribeAttempts = 3;
    private int _subscribeAttempts;
    private TimeSpan _backOffTimeSpan = TimeSpan.FromSeconds(1);

    public SqlStreamStoreDroppedSubscriptionHandler(Action stop, Func<Task> resubscribe)
    {
        _stop = stop;
        _resubscribe = resubscribe;
    }

    public void HandleDroppedSubscription(SubscriptionDroppedReason reason, Exception exception)
    {
        switch (reason)
        {
            case SubscriptionDroppedReason.Disposed:
                Log.LogInformation("Subscription was disposed. Stopping event publisher");
                _stop();
                break;
            case SubscriptionDroppedReason.SubscriberError:
                Log.LogCritical(exception,
                    $"Exception occurred in subscriber. Stopping event publisher. Reason {reason}");
                _stop();
                break;
            case SubscriptionDroppedReason.StreamStoreError:
                Log.LogCritical(exception,
                    $"Server error in SqlStreamStore. Stopping event publisher. Reason {reason}");
                _stop();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(reason), reason, null);
        }
    }

    // TODO: Need to better understand the exceptions that are thrown to understand when to resubscribe
    private async Task TryToResubscribe()
    {
        if (_subscribeAttempts > _maxSubscribeAttempts)
        {
            Log.LogCritical($"Unable to reconnect to SqlStreamStore after {_maxSubscribeAttempts} attempts. Stopping event publisher");
            _stop();
            return;
        }

        Log.LogInformation($"Waiting for {_backOffTimeSpan.TotalSeconds} seconds before resubscribing. Resubscribe attempt {_subscribeAttempts}");
        await Task.Delay(_backOffTimeSpan);

        _backOffTimeSpan *= 2;
        _subscribeAttempts++;
        await _resubscribe();
    }
}