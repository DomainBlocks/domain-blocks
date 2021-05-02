using System;
using System.Threading.Tasks;
using DomainLib.Common;
using EventStore.ClientAPI;
using Microsoft.Extensions.Logging;

namespace DomainLib.Projections.EventStore
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
        
        public void HandleDroppedSubscription(SubscriptionDropReason reason, Exception exception)
        {
            switch (reason)
            {
                case SubscriptionDropReason.UserInitiated:
                    Log.LogInformation("User initiated EventStore subscription drop. Stopping event publisher");
                    _stop();
                    break;
                case SubscriptionDropReason.NotAuthenticated:
                case SubscriptionDropReason.AccessDenied:
                    Log.LogCritical(exception,
                                    $"Error subscribing to EventStore. Stopping event publisher. Reason {reason}");
                    _stop();
                    break;
                case SubscriptionDropReason.SubscribingError:
                case SubscriptionDropReason.CatchUpError:
                case SubscriptionDropReason.ServerError:
                case SubscriptionDropReason.EventHandlerException:
                case SubscriptionDropReason.Unknown:
                case SubscriptionDropReason.NotFound:
                    Log.LogCritical(exception,
                                    $"Exception occurred in EventStore subscription. Stopping event publisher. Reason {reason}");
                    _stop();
                    break;
                case SubscriptionDropReason.PersistentSubscriptionDeleted:
                    Log.LogError(exception, "Persistent subscription was deleted. Stopping event publisher");
                    _stop();
                    break;
                case SubscriptionDropReason.ConnectionClosed:
                    Log.LogCritical(exception, "Underlying EventStore connection was closed. Stopping event publisher");
                    _stop();
                    break;
                case SubscriptionDropReason.ProcessingQueueOverflow:
                case SubscriptionDropReason.MaxSubscribersReached:
                    Log.LogWarning(exception, "Transient error in EventStore connection. Attempting to resubscribe");
                    TryToResubscribe().Wait();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(reason), reason, null);
            }
        }
        
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