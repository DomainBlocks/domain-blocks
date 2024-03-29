﻿using DomainBlocks.ThirdParty.SqlStreamStore.Streams;

namespace DomainBlocks.ThirdParty.SqlStreamStore.Subscriptions
{
    /// <summary>
    ///       Represents a delegate that is invoked when a stream messages has been received in a subscription.
    /// </summary>
    /// <param name="subscription">
    ///      The source subscription.
    /// </param>
    /// <param name="streamMessage">
    ///     The stream message.
    /// </param>
    /// <param name="cancellationToken">
    ///     The cancellation instruction.
    /// </param>
    /// <returns>A task that represents the asynchronous handling of the stream message.</returns>
    public delegate Task StreamMessageReceived(IStreamSubscription subscription, StreamMessage streamMessage, CancellationToken cancellationToken);
}