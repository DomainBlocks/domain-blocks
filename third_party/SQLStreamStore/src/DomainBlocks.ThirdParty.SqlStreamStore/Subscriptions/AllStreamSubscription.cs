﻿using DomainBlocks.Logging;
using DomainBlocks.ThirdParty.SqlStreamStore.Imports.AsyncEx.Nito.AsyncEx.Coordination;
using DomainBlocks.ThirdParty.SqlStreamStore.Infrastructure;
using DomainBlocks.ThirdParty.SqlStreamStore.Streams;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.ThirdParty.SqlStreamStore.Subscriptions
{
    /// <summary>
    ///     Represents a subscription to all streams.
    /// </summary>

    public sealed class AllStreamSubscription : IAllStreamSubscription
    {
        public const int DefaultPageSize = 10;
        private static readonly ILogger<AllStreamSubscription> Logger = LogProvider.Get<AllStreamSubscription>();
        private int _pageSize = DefaultPageSize;
        private long _nextPosition;
        private readonly IReadonlyStreamStore _readonlyStreamStore;
        private readonly AllStreamMessageReceived _streamMessageReceived;
        private readonly bool _prefetchJsonData;
        private readonly HasCaughtUp _hasCaughtUp;
        private readonly AllSubscriptionDropped _subscriptionDropped;
        private readonly IDisposable _notification;
        private readonly CancellationTokenSource _disposed = new CancellationTokenSource();
        private readonly AsyncAutoResetEvent _streamStoreNotification = new AsyncAutoResetEvent();
        private readonly TaskCompletionSource<object> _started = new TaskCompletionSource<object>();
        private readonly InterlockedBoolean _notificationRaised = new InterlockedBoolean();

        public AllStreamSubscription(
            long? continueAfterPosition,
            IReadonlyStreamStore readonlyStreamStore,
            IObservable<Unit> streamStoreAppendedNotification,
            AllStreamMessageReceived streamMessageReceived,
            AllSubscriptionDropped subscriptionDropped,
            HasCaughtUp hasCaughtUp,
            bool prefetchJsonData,
            string name)
        {
            FromPosition = continueAfterPosition;
            LastPosition = continueAfterPosition;
            _nextPosition = continueAfterPosition + 1 ?? Position.Start;
            _readonlyStreamStore = readonlyStreamStore;
            _streamMessageReceived = streamMessageReceived;
            _prefetchJsonData = prefetchJsonData;
            _subscriptionDropped = subscriptionDropped ?? ((_, __, ___) => { });
            _hasCaughtUp = hasCaughtUp ?? (_ => { });
            Name = string.IsNullOrWhiteSpace(name) ? Guid.NewGuid().ToString() : name;

            readonlyStreamStore.OnDispose += ReadonlyStreamStoreOnOnDispose;

            _notification = streamStoreAppendedNotification.Subscribe(_ =>
            {
                _streamStoreNotification.Set();
            });

            Task.Run(PullAndPush);

            Logger.LogInformation(
                "AllStream subscription created {name} continuing after position {position}",
                Name,
                continueAfterPosition?.ToString() ?? "<null>");
        }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public long? FromPosition { get; }

        /// <inheritdoc />
        public long? LastPosition { get; private set; }

        /// <inheritdoc />
        public Task Started => _started.Task;

        /// <inheritdoc />
        public int MaxCountPerRead
        {
            get => _pageSize;
            set => _pageSize = value <= 0 ? 1 : value;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed.IsCancellationRequested)
            {
                return;
            }
            _disposed.Cancel();
            _notification.Dispose();
        }

        private void ReadonlyStreamStoreOnOnDispose()
        {
            _readonlyStreamStore.OnDispose -= ReadonlyStreamStoreOnOnDispose;
            Dispose();
        }

        private async Task PullAndPush()
        {
            if (FromPosition == Position.End)
            {
                await Initialize();
            }
            _started.SetResult(null);

            while (true)
            {
                bool pause = false;
                bool? lastHasCaughtUp = null;

                while (!pause)
                {
                    var page = await Pull();

                    await Push(page);

                    if ((!lastHasCaughtUp.HasValue && page.IsEnd) ||
                       ((!lastHasCaughtUp.HasValue || lastHasCaughtUp.Value != page.IsEnd)
                       && page.Messages.Length > 0))
                    {
                        // Only raise if the state changes and there were messages read
                        lastHasCaughtUp = page.IsEnd;
                        _hasCaughtUp(page.IsEnd);
                    }

                    pause = page.IsEnd && page.Messages.Length == 0;
                }

                // Wait for notification before starting again.
                try
                {
                    await _streamStoreNotification.WaitAsync(_disposed.Token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    NotifySubscriptionDropped(SubscriptionDroppedReason.Disposed);
                    throw;
                }
            }
        }

        private async Task Initialize()
        {
            long headPosition;
            try
            {
                // Get the last stream version and subscribe from there.
                headPosition = await _readonlyStreamStore.ReadHeadPosition(_disposed.Token)
                    .ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                NotifySubscriptionDropped(SubscriptionDroppedReason.Disposed);
                throw;
            }
            catch (OperationCanceledException)
            {
                NotifySubscriptionDropped(SubscriptionDroppedReason.Disposed);
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error reading stream {Name}", Name);
                NotifySubscriptionDropped(SubscriptionDroppedReason.StreamStoreError, ex);
                throw;
            }

            // Only new Messages, i.e. the one after the current last one.
            // Edge case for empty store where Next position 0 (when FromPosition = 0)
            _nextPosition = headPosition == 0 ? 0 : headPosition + 1;
        }

        private async Task<ReadAllPage> Pull()
        {
            ReadAllPage readAllPage;
            try
            {
                readAllPage = await _readonlyStreamStore
                    .ReadAllForwards(_nextPosition, MaxCountPerRead, _prefetchJsonData, _disposed.Token)
                    .ConfigureAwait(false);
            }
            catch(ObjectDisposedException)
            {
                NotifySubscriptionDropped(SubscriptionDroppedReason.Disposed);
                throw;
            }
            catch (OperationCanceledException)
            {
                NotifySubscriptionDropped(SubscriptionDroppedReason.Disposed);
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error reading all stream {Name}", Name);
                NotifySubscriptionDropped(SubscriptionDroppedReason.StreamStoreError, ex);
                throw;
            }
            return readAllPage;
        }

        private async Task Push(ReadAllPage page)
        {
            foreach (var message in page.Messages)
            {
                if (_disposed.IsCancellationRequested)
                {
                    NotifySubscriptionDropped(SubscriptionDroppedReason.Disposed);
                    _disposed.Token.ThrowIfCancellationRequested();
                }
                _nextPosition = message.Position + 1;
                LastPosition = message.Position;
                try
                {
                    await _streamMessageReceived(this, message, _disposed.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.LogError(
                        ex,
                        "Exception with subscriber receiving message {Name}" +
                        "Message: {message}.", Name, message);
                    NotifySubscriptionDropped(SubscriptionDroppedReason.SubscriberError, ex);
                    throw;
                }
            }
        }

        private void NotifySubscriptionDropped(SubscriptionDroppedReason reason, Exception exception = null)
        {
            if (_notificationRaised.CompareExchange(true, false))
            {
                return;
            }
            try
            {
                Logger.LogInformation(exception, "All stream subscription dropped {Name}. Reason: {reason}", Name, reason);
                _subscriptionDropped.Invoke(this, reason, exception);
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    ex,
                    "Error notifying subscriber that subscription has been dropped ({Name}).",
                    Name);
            }
        }
    }
}
