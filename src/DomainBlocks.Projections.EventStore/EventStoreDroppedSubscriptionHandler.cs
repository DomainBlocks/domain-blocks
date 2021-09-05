using System;
using System.Threading.Tasks;
using DomainBlocks.Common;
using EventStore.Client;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.Projections.EventStore
{
    public class EventStoreDroppedSubscriptionHandler
    {
        private readonly Action _stop;
        private readonly Func<Task> _resubscribe;
        private static readonly ILogger<EventStoreDroppedSubscriptionHandler> Log = Logger.CreateFor<EventStoreDroppedSubscriptionHandler>();

        private int _maxSubscribeAttempts = 3;
        private int _subscribeAttempts;
        private TimeSpan _backOffTimeSpan = TimeSpan.FromSeconds(1);

        public EventStoreDroppedSubscriptionHandler(Action stop, Func<Task> resubscribe)
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
                case SubscriptionDroppedReason.ServerError:
                    Log.LogCritical(exception,
                                    $"Server error in EventStore subscription. Stopping event publisher. Reason {reason}");
                    _stop();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(reason), reason, null);
            }
        }
        
        // TODO: Need to better understand the new reasons for subscriptions being dropped
        // to see if it still makes sense to try resubscribing
        private async Task TryToResubscribe()
        {
            if (_subscribeAttempts > _maxSubscribeAttempts)
            {
                Log.LogCritical($"Unable to reconnect to EventStore after {_maxSubscribeAttempts} attempts. Stopping event publisher");
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
}