﻿using DomainBlocks.Logging;
using DomainBlocks.ThirdParty.SqlStreamStore.Imports.AsyncEx.Nito.AsyncEx.Coordination;
using DomainBlocks.ThirdParty.SqlStreamStore.Infrastructure;
using DomainBlocks.ThirdParty.SqlStreamStore.Streams;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.ThirdParty.SqlStreamStore.Subscriptions
{
    /// <summary>
    ///     Represents a subscription to a stream.
    /// </summary>
    public sealed class StreamSubscription: IStreamSubscription
    {
        /// <summary>
        ///     The default page size to read.
        /// </summary>
        public const int DefaultPageSize = 10;
        private static readonly ILogger<StreamSubscription> Logger = LogProvider.Get<StreamSubscription>();

        private int _pageSize = DefaultPageSize;
        private int _nextVersion;
        private readonly int? _continueAfterVersion;
        private readonly IReadonlyStreamStore _readonlyStreamStore;
        private readonly StreamMessageReceived _streamMessageReceived;
        private readonly bool _prefectchJsonData;
        private readonly HasCaughtUp _hasCaughtUp;
        private readonly SubscriptionDropped _subscriptionDropped;
        private readonly IDisposable _notification;
        private readonly CancellationTokenSource _disposed = new CancellationTokenSource();
        private readonly AsyncAutoResetEvent _streamStoreNotification = new AsyncAutoResetEvent();
        private readonly TaskCompletionSource<object> _started = new TaskCompletionSource<object>();
        private readonly InterlockedBoolean _notificationRaised = new InterlockedBoolean();

        public StreamSubscription(
            string streamId,
            int? continueAfterVersion,
            IReadonlyStreamStore readonlyStreamStore,
            IObservable<Unit> streamStoreAppendedNotification,
            StreamMessageReceived streamMessageReceived,
            SubscriptionDropped subscriptionDropped,
            HasCaughtUp hasCaughtUp,
            bool prefectchJsonData,
            string name)
        {
            StreamId = streamId;
            _continueAfterVersion = continueAfterVersion;
            _readonlyStreamStore = readonlyStreamStore;
            _streamMessageReceived = streamMessageReceived;
            _prefectchJsonData = prefectchJsonData;
            _subscriptionDropped = subscriptionDropped ?? ((_, __, ___) => { });
            _hasCaughtUp = hasCaughtUp ?? ((_) => { });
            Name = string.IsNullOrWhiteSpace(name) ? Guid.NewGuid().ToString() : name;

            readonlyStreamStore.OnDispose += ReadonlyStreamStoreOnOnDispose;

            _notification = streamStoreAppendedNotification.Subscribe(_ =>
            {
                _streamStoreNotification.Set();
            });

            Task.Run(PullAndPush);

            Logger.LogInformation(
                "Stream subscription created {name} continuing after version {version}",
                name,
                continueAfterVersion?.ToString() ?? "<null>");
        }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public string StreamId { get; }

        /// <inheritdoc />
        public int? LastVersion { get; private set; }

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
            NotifySubscriptionDropped(SubscriptionDroppedReason.Disposed);
        }

        private void ReadonlyStreamStoreOnOnDispose()
        {
            _readonlyStreamStore.OnDispose -= ReadonlyStreamStoreOnOnDispose;
            Dispose();
        }

        private async Task PullAndPush()
        {
            if (!_continueAfterVersion.HasValue)
            {
                _nextVersion = 0;
            }
            else if (_continueAfterVersion.Value == StreamVersion.End)
            {
                await Initialize();
            }
            else
            {
                _nextVersion = _continueAfterVersion.Value + 1;
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
                        ((!lastHasCaughtUp.HasValue || lastHasCaughtUp.Value != page.IsEnd)))
                    {
                        // Only raise if the state changes
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
                catch(TaskCanceledException)
                {
                    NotifySubscriptionDropped(SubscriptionDroppedReason.Disposed);
                    throw;
                }
            }
        }

        private async Task Initialize()
        {
            ReadStreamPage eventsPage;
            try
            {
                // Get the last stream version and subscribe from there.
                eventsPage = await _readonlyStreamStore.ReadStreamBackwards(
                    StreamId,
                    StreamVersion.End,
                    1,
                    _disposed.Token).ConfigureAwait(false);
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
                Logger.LogError(ex, "Error reading stream {Name}/{StreamId}", Name, StreamId);
                NotifySubscriptionDropped(SubscriptionDroppedReason.StreamStoreError, ex);
                throw;
            }

            //Only new Messages, i.e. the one after the current last one
            _nextVersion = eventsPage.LastStreamVersion + 1;
            LastVersion = _nextVersion;
        }

        private async Task<ReadStreamPage> Pull()
        {
            ReadStreamPage readStreamPage;
            try
            {
                readStreamPage = await _readonlyStreamStore
                    .ReadStreamForwards(StreamId, _nextVersion, MaxCountPerRead, _prefectchJsonData, _disposed.Token)
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
                Logger.LogError(ex, "Error reading stream {Name}/{StreamId}", Name, StreamId);
                NotifySubscriptionDropped(SubscriptionDroppedReason.StreamStoreError, ex);
                throw;
            }
            return readStreamPage;
        }

        private async Task Push(ReadStreamPage page)
        {
            foreach (var message in page.Messages)
            {
                if (_disposed.IsCancellationRequested)
                {
                    NotifySubscriptionDropped(SubscriptionDroppedReason.Disposed);
                    _disposed.Token.ThrowIfCancellationRequested();
                }
                _nextVersion = message.StreamVersion + 1;
                LastVersion = message.StreamVersion;
                try
                {
                    await _streamMessageReceived(this, message, _disposed.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.LogError(
                        ex,
                        "Exception with subscriber receiving message {Name}/{StreamId}" +
                        "Message: {message}.", Name, StreamId, message);
                    NotifySubscriptionDropped(SubscriptionDroppedReason.SubscriberError, ex);
                    throw;
                }
            }
        }

        private void NotifySubscriptionDropped(SubscriptionDroppedReason reason, Exception exception = null)
        {
            if(_notificationRaised.CompareExchange(true, false))
            {
                return;
            }
            try
            {
                Logger.LogInformation(
                    exception,
                    "Subscription dropped {Name}/{StreamId}. Reason: {reason}",
                    Name, StreamId, reason);

                _subscriptionDropped.Invoke(this, reason, exception);
            }
            catch(Exception ex)
            {
                Logger.LogError(
                    ex,
                    "Error notifying subscriber that subscription has been dropped ({Name}/{StreamId}).",
                    Name, StreamId);
            }
        }
    }
}
