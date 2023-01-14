using DomainBlocks.ThirdParty.SqlStreamStore.Subscriptions;
using DomainBlocks.Logging;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.SqlStreamStore.Projections;

public class SqlStreamStoreDroppedSubscriptionHandler
{
    private static readonly ILogger<SqlStreamStoreDroppedSubscriptionHandler> Logger =
        Log.Create<SqlStreamStoreDroppedSubscriptionHandler>();

    private const int MaxSubscribeAttempts = 3;
    private readonly Action _stop;
    private readonly Func<Task> _resubscribe;
    private int _subscribeAttempts;
    private TimeSpan _backOffTimeSpan = TimeSpan.FromSeconds(1);

    public SqlStreamStoreDroppedSubscriptionHandler(Action stop, Func<Task> resubscribe)
    {
        _stop = stop;
        _resubscribe = resubscribe;
    }

    public void HandleDroppedSubscription(SubscriptionDroppedReason reason, Exception? exception)
    {
        switch (reason)
        {
            case SubscriptionDroppedReason.Disposed:
                Logger.LogInformation("Subscription was disposed. Stopping event publisher");
                _stop();
                break;
            case SubscriptionDroppedReason.SubscriberError:
                Logger.LogCritical(
                    exception, "Exception occurred in subscriber. Stopping event publisher. Reason {Reason}", reason);
                _stop();
                break;
            case SubscriptionDroppedReason.StreamStoreError:
                Logger.LogCritical(exception,
                    "Server error in SqlStreamStore. Stopping event publisher. Reason {Reason}", reason);
                _stop();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(reason), reason, null);
        }
    }

    // TODO: Need to better understand the exceptions that are thrown to understand when to resubscribe
    private async Task TryToResubscribe()
    {
        if (_subscribeAttempts > MaxSubscribeAttempts)
        {
            Logger.LogCritical(
                $"Unable to reconnect to SqlStreamStore after {MaxSubscribeAttempts} attempts. Stopping event publisher");
            _stop();
            return;
        }

        Logger.LogInformation(
            $"Waiting for {_backOffTimeSpan.TotalSeconds} seconds before resubscribing. Resubscribe attempt {_subscribeAttempts}");
        await Task.Delay(_backOffTimeSpan);

        _backOffTimeSpan *= 2;
        _subscribeAttempts++;
        await _resubscribe();
    }
}