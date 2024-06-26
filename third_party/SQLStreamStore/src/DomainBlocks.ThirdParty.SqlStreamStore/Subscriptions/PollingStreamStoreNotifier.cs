﻿using DomainBlocks.Logging;
using DomainBlocks.ThirdParty.SqlStreamStore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.ThirdParty.SqlStreamStore.Subscriptions
{
    /// <summary>
    ///     Represents an implementation of <see cref="IStreamStoreNotifier"/> that polls
    ///     the target stream store for new message.
    /// </summary>
    public sealed class PollingStreamStoreNotifier : IStreamStoreNotifier
    {
        private static readonly ILogger<PollingStreamStoreNotifier> Logger = LogProvider.Get<PollingStreamStoreNotifier>();

        private readonly CancellationTokenSource _disposed = new CancellationTokenSource();
        private readonly Func<CancellationToken, Task<long>> _readHeadPosition;
        private readonly int _interval;
        private readonly Subject<Unit> _storeAppended = new Subject<Unit>();

        /// <summary>
        ///     Initializes a new instance of of <see cref="PollingStreamStoreNotifier"/>.
        /// </summary>
        /// <param name="readonlyStreamStore">The store to poll.</param>
        /// <param name="interval">The interval to poll in milliseconds. Default is 1000.</param>
        public PollingStreamStoreNotifier(IReadonlyStreamStore readonlyStreamStore, int interval = 1000)
            : this(readonlyStreamStore.ReadHeadPosition, interval)
        {}

        /// <summary>
        ///     Initializes a new instance of of <see cref="PollingStreamStoreNotifier"/>.
        /// </summary>
        /// <param name="readHeadPosition">An operation to read the head position of a store.</param>
        /// <param name="interval">The interval to poll in milliseconds. Default is 1000.</param>
        public PollingStreamStoreNotifier(Func<CancellationToken, Task<long>> readHeadPosition, int interval = 1000)
        {
            _readHeadPosition = readHeadPosition;
            _interval = interval;
            Task.Run(Poll, _disposed.Token);
        }

        public void Dispose()
        {
            _disposed.Cancel();
        }

        /// <inheritdoc />
        public IDisposable Subscribe(IObserver<Unit> observer) => _storeAppended.Subscribe(observer);

        private async Task Poll()
        {
            long headPosition = -1;
            long previousHeadPosition = headPosition;
            while (!_disposed.IsCancellationRequested)
            {
                try
                {
                    headPosition = await _readHeadPosition(_disposed.Token);
                    if(Logger.IsEnabled(LogLevel.Trace))
                    {
                        Logger.LogTrace("Polling head position {headPosition}. Previous {previousHeadPosition}",
                            headPosition, previousHeadPosition);
                    }
                }
                catch(Exception ex)
                {
                    Logger.LogError(ex, "Exception occurred polling stream store for messages. " +
                                        "HeadPosition: {headPosition}", headPosition);
                }

                if(headPosition > previousHeadPosition)
                {
                    _storeAppended.OnNext(Unit.Default);
                    previousHeadPosition = headPosition;
                }
                else
                {
                    await Task.Delay(_interval, _disposed.Token);
                }
            }
        }
    }
}