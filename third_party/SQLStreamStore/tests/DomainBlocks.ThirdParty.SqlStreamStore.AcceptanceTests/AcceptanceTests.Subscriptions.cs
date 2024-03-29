﻿using DomainBlocks.ThirdParty.SqlStreamStore;
using DomainBlocks.ThirdParty.SqlStreamStore.Imports.AsyncEx.Nito.AsyncEx.Coordination;
using DomainBlocks.ThirdParty.SqlStreamStore.Streams;
using DomainBlocks.ThirdParty.SqlStreamStore.Subscriptions;

namespace SqlStreamStore
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Shouldly;
    using DomainBlocks.ThirdParty.SqlStreamStore.TestUtils;
    using Xunit;

    public partial class AcceptanceTests
    {
        [Fact, Trait("Category", "Subscriptions")]
        public async Task Can_subscribe_to_a_stream_from_start()
        {
            var streamId1 = "stream-1";
            await AppendMessages(Store, streamId1, 10);

            var streamId2 = "stream-2";
            await AppendMessages(Store, streamId2, 10);

            var done = new TaskCompletionSource<StreamMessage>();
            var receivedMessages = new List<StreamMessage>();
            using (var subscription = Store.SubscribeToStream(
                streamId1,
                StreamVersion.None,
                (_, message, ct) =>
                {
                    receivedMessages.Add(message);
                    if (message.StreamVersion == 11)
                    {
                        done.SetResult(message);
                    }
                    return Task.CompletedTask;
                }))
            {
                await AppendMessages(Store, streamId1, 2);

                var receivedMessage = await done.Task.WithTimeout();

                receivedMessages.Count.ShouldBe(12);
                subscription.StreamId.ShouldBe(streamId1);
                receivedMessage.StreamId.ShouldBe(streamId1);
                receivedMessage.StreamVersion.ShouldBe(11);
                subscription.LastVersion.Value.ShouldBeGreaterThan(0);
            }
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task When_subscribe_to_a_stream_and_receive_message_then_should_get_subscription_instance()
        {
            var streamId = "stream-1";
            await AppendMessages(Store, streamId, 10);

            var done = new TaskCompletionSource<IStreamSubscription>();
            using (var subscription = Store.SubscribeToStream(
                streamId,
                StreamVersion.None,
                (sub, _, ct) =>
                {
                    done.SetResult(sub);
                    return Task.CompletedTask;
                }))
            {
                var receivedSubscription = await done.Task.WithTimeout();

                receivedSubscription.ShouldBe(subscription);
            }
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task Can_subscribe_to_a_stream_from_start_before_messages_are_written()
        {
            var streamId = "stream-1";
            var done = new TaskCompletionSource<StreamMessage>();
            var receivedMessages = new List<StreamMessage>();

            using (var subscription = Store.SubscribeToStream(
                streamId,
                StreamVersion.None,
                (_, message, ct) =>
                {
                    receivedMessages.Add(message);
                    if (message.StreamVersion == 1)
                    {
                        done.SetResult(message);
                    }
                    return Task.CompletedTask;
                }))
            {
                await AppendMessages(Store, streamId, 2);

                var receivedMessage = await done.Task.WithTimeout();

                receivedMessages.Count.ShouldBe(2);
                subscription.StreamId.ShouldBe(streamId);
                receivedMessage.StreamId.ShouldBe(streamId);
                receivedMessage.StreamVersion.ShouldBe(1);
                subscription.LastVersion.Value.ShouldBeGreaterThan(0);
            }
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task Can_subscribe_to_all_stream_from_start()
        {
            string streamId1 = "stream-1";
            await AppendMessages(Store, streamId1, 3);

            string streamId2 = "stream-2";
            await AppendMessages(Store, streamId2, 3);

            var receiveMessages = new TaskCompletionSource<StreamMessage>();
            List<StreamMessage> receivedMessages = new List<StreamMessage>();
            using(Store.SubscribeToAll(
                Position.None,
                (_, message, __) =>
                {
                    TestOutputHelper.WriteLine($"Received message {message.StreamId} " +
                                                $"{message.StreamVersion} {message.Position}");
                    receivedMessages.Add(message);
                    if (message.StreamId == streamId1 && message.StreamVersion == 3)
                    {
                        receiveMessages.SetResult(message);
                    }
                    return Task.CompletedTask;
                }))
            {
                await AppendMessages(Store, streamId1, 1);

                await receiveMessages.Task.WithTimeout();

                receivedMessages.Count.ShouldBe(7);
            }
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task When_subscribe_to_all_and_receive_message_then_should_get_subscription_instance()
        {
            const string streamId = "stream-1";
            await AppendMessages(Store, streamId, 10);

            var done = new TaskCompletionSource<IAllStreamSubscription>();
            using (var subscription = Store.SubscribeToAll(
                Position.None,
                (sub, _, __) =>
                {
                    done.SetResult(sub);
                    return Task.CompletedTask;
                }))
            {
                var receivedSubscription = await done.Task.WithTimeout();

                receivedSubscription.ShouldBe(subscription);
            }
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task Can_subscribe_to_all_stream_from_start_before_messages_are_written()
        {
            string streamId1 = "stream-1";

            string streamId2 = "stream-2";

            var receiveMessages = new TaskCompletionSource<StreamMessage>();
            List<StreamMessage> receivedMessages = new List<StreamMessage>();
            using (Store.SubscribeToAll(
                Position.None,
                (_, message, __) =>
                {
                    TestOutputHelper.WriteLine($"Received message {message.StreamId} {message.StreamVersion} {message.Position}");
                    receivedMessages.Add(message);
                    if (message.StreamId == streamId1 && message.StreamVersion == 3)
                    {
                        receiveMessages.SetResult(message);
                    }
                    return Task.CompletedTask;
                }))
            {
                await AppendMessages(Store, streamId1, 3);

                await AppendMessages(Store, streamId2, 3);

                await AppendMessages(Store, streamId1, 1);

                await receiveMessages.Task.WithTimeout(5000);

                receivedMessages.Count.ShouldBe(7);
            }
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task Can_subscribe_to_a_stream_from_end()
        {
            string streamId1 = "stream-1";
            await AppendMessages(Store, streamId1, 10);

            string streamId2 = "stream-2";
            await AppendMessages(Store, streamId2, 10);

            var receiveMessage = new TaskCompletionSource<StreamMessage>();
            int receivedCount = 0;
            using (var subscription = Store.SubscribeToStream(
                streamId1,
                StreamVersion.End,
                (_, message, ct) =>
                {
                    TestOutputHelper.WriteLine($"Received message {message.StreamId} {message.StreamVersion} "
                                                + $"{message.Position}");
                    receivedCount++;
                    if (message.StreamVersion >= 11)
                    {
                        receiveMessage.SetResult(message);
                    }
                    return Task.CompletedTask;
                }))
            {
                await subscription.Started;
                await AppendMessages(Store, streamId1, 2);

                var allMessagesPage = await Store.ReadAllForwards(0, 30);
                foreach(var streamMessage in allMessagesPage.Messages)
                {
                    TestOutputHelper.WriteLine(streamMessage.ToString());
                }

                var receivedMessage = await receiveMessage.Task.WithTimeout();

                receivedCount.ShouldBe(2);
                subscription.StreamId.ShouldBe(streamId1);
                receivedMessage.StreamId.ShouldBe(streamId1);
                receivedMessage.StreamVersion.ShouldBe(11);
                subscription.LastVersion.Value.ShouldBeGreaterThan(0);
            }
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task Given_non_empty_streamstore_can_subscribe_to_all_stream_from_end()
        {
            string streamId1 = "stream-1";
            await AppendMessages(Store, streamId1, 10);

            string streamId2 = "stream-2";
            await AppendMessages(Store, streamId2, 10);

            var receiveMessages = new TaskCompletionSource<StreamMessage>();
            List<StreamMessage> receivedMessages = new List<StreamMessage>();
            using(var subscription = Store.SubscribeToAll(
                Position.End,
                (_, message, __) =>
                {
                    TestOutputHelper.WriteLine($"StreamId={message.StreamId} Version={message.StreamVersion} "
                                                + $"Position={message.Position}");
                    receivedMessages.Add(message);
                    if (message.StreamId == streamId1 && message.StreamVersion == 11)
                    {
                        receiveMessages.SetResult(message);
                    }
                    return Task.CompletedTask;
                }))
            {
                await subscription.Started;

                await AppendMessages(Store, streamId1, 2);

                await receiveMessages.Task.WithTimeout();

                receivedMessages.Count.ShouldBe(2);
            }
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task Given_empty_streamstore_can_subscribe_to_all_stream_from_end()
        {
            string streamId1 = "stream-1";
            var receiveMessages = new TaskCompletionSource<int>();
            List<StreamMessage> receivedMessages = new List<StreamMessage>();
            using (var subscription = Store.SubscribeToAll(
                Position.End,
                (_, message, __) =>
                {
                    receivedMessages.Add(message);
                    if (message.StreamId == streamId1 && message.StreamVersion == 9)
                    {
                        receiveMessages.SetResult(0);
                    }
                    return Task.CompletedTask;
                }))
            {
                await subscription.Started;

                await AppendMessages(Store, streamId1, 10);

                await receiveMessages.Task.WithTimeout();

                receivedMessages.Count.ShouldBe(10);
            }
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task Can_subscribe_to_a_stream_from_a_specific_version()
        {
            string streamId1 = "stream-1";
            await AppendMessages(Store, streamId1, 10);

            string streamId2 = "stream-2";
            await AppendMessages(Store, streamId2, 10);

            var receiveMessages = new TaskCompletionSource<StreamMessage>();
            int receivedCount = 0;
            using (var subscription = Store.SubscribeToStream(
                streamId1,
                7,
                (_, message, ct) =>
                {
                    receivedCount++;
                    if (message.StreamVersion == 11)
                    {
                        receiveMessages.SetResult(message);
                    }
                    return Task.CompletedTask;
                }))
            {
                await AppendMessages(Store, streamId1, 2);

                var receivedMessage = await receiveMessages.Task.WithTimeout();

                receivedCount.ShouldBe(4);
                subscription.StreamId.ShouldBe(streamId1);
                receivedMessage.StreamId.ShouldBe(streamId1);
                receivedMessage.StreamVersion.ShouldBe(11);
                subscription.LastVersion.Value.ShouldBeGreaterThan(0);
            }
        }

        [Fact(Skip = "Hanging on CI. DH to investigate"), Trait("Category", "Subscriptions")]
        public async Task Can_have_multiple_subscriptions_to_all()
        {
            string streamId1 = "stream-1";
            await AppendMessages(Store, streamId1, 2);

            var completionSources =
                Enumerable.Range(0, Fixture.MaxSubscriptionCount).Select(_ => new TaskCompletionSource<int>())
                .ToArray();

            var subscriptions = Enumerable.Range(0, Fixture.MaxSubscriptionCount)
                .Select(index => Store.SubscribeToAll(
                    Position.None,
                    (_, message, __) =>
                    {
                        if(message.StreamVersion == 1)
                        {
                            completionSources[index].SetResult(0);
                        }
                        return Task.CompletedTask;
                    }))
                .ToArray();

            try
            {
                await Task.WhenAll(completionSources.Select(source => source.Task)).WithTimeout(10000);
            }
            finally
            {
                foreach (var subscription in subscriptions) subscription.Dispose();
            }
        }

        [Fact(Skip="Hanging on CI. DH to investigate"), Trait("Category", "Subscriptions")]
        public async Task Can_have_multiple_subscriptions_to_stream()
        {
            string streamId1 = "stream-1";
            await AppendMessages(Store, streamId1, 2);

            var subscriptionCount = 50;

            var completionSources =
                Enumerable.Range(0, subscriptionCount)
                .Select(_ => new TaskCompletionSource<int>())
                .ToArray();

            var subscriptions = Enumerable.Range(0, subscriptionCount)
                .Select(index => Store.SubscribeToStream(
                    streamId1,
                    0,
                    (_, message, ct) =>
                    {
                        if(message.StreamVersion == 1)
                        {
                            completionSources[index].SetResult(0);
                        }
                        return Task.CompletedTask;
                    }))
                .ToArray();

            try
            {
                await Task.WhenAll(completionSources.Select(source => source.Task)).WithTimeout();
            }
            finally
            {
                foreach(var subscription in subscriptions)
                {
                    subscription.Dispose();
                }
            }
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task When_delete_then_deleted_message_should_have_correct_position()
        {
            // Arrange
            var streamId = "stream-1";

            var receiveMessage = new TaskCompletionSource<bool>();
            var receiveDeletedMessage = new TaskCompletionSource<StreamMessage>();
            List<StreamMessage> receivedMessages = new List<StreamMessage>();
            using (var subscription = Store.SubscribeToAll(
                Position.None,
                (_, message, __) =>
                {
                    TestOutputHelper.WriteLine($"Received message {message.StreamId} " +
                                                $"{message.StreamVersion} {message.Position}");
                    receivedMessages.Add(message);
                    if (message.StreamId == streamId)
                    {
                        receiveMessage.SetResult(true);
                    }
                    if (message.StreamId == Deleted.DeletedStreamId
                        && message.Type == Deleted.StreamDeletedMessageType)
                    {
                        receiveDeletedMessage.SetResult(message);
                    }
                    return Task.CompletedTask;
                }))
            {
                await AppendMessages(Store, streamId, 1);
                await receiveMessage.Task.WithTimeout();


                // Act
                await Store.DeleteStream(streamId);
                await receiveDeletedMessage.Task.WithTimeout();

                // Assert
                receivedMessages[1].Position.ShouldBeGreaterThan(receivedMessages[0].Position);
            }
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task When_exception_throw_by_stream_subscriber_then_should_drop_subscription_with_reason_SubscriberError()
        {
            var eventReceivedException = new TaskCompletionSource<SubscriptionDroppedReason>();
            Task MessageReceived(IStreamSubscription _, StreamMessage __, CancellationToken ___) => throw new Exception();
            void SubscriptionDropped(IStreamSubscription _, SubscriptionDroppedReason reason, Exception __) => eventReceivedException.SetResult(reason);
            var streamId = "stream-1";

            using(Store.SubscribeToStream(
                streamId,
                StreamVersion.None,
                MessageReceived,
                SubscriptionDropped))
            {
                await Store.AppendToStream(streamId,
                    ExpectedVersion.NoStream,
                    new NewStreamMessage(Guid.NewGuid(), "type", "{}"));

                var droppedReason = await eventReceivedException.Task.WithTimeout();

                droppedReason.ShouldBe(SubscriptionDroppedReason.SubscriberError);
            }
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task When_stream_subscription_disposed_then_should_drop_subscription_with_reason_Disposed()
        {
            var tcs = new TaskCompletionSource<SubscriptionDroppedReason>();
            var subscription = Store.SubscribeToStream(
                "stream-1",
                StreamVersion.End,
                (_, __, ___) => Task.CompletedTask,
                (_, reason, __) =>
                {
                    tcs.SetResult(reason);
                });
            subscription.Dispose();
            var droppedReason = await tcs.Task.WithTimeout();

            droppedReason.ShouldBe(SubscriptionDroppedReason.Disposed);
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task When_stream_subscription_dropped_then_should_supply_subscription_instance()
        {
            var tcs = new TaskCompletionSource<IStreamSubscription>();
            var subscription = Store.SubscribeToStream(
                "stream-1",
                StreamVersion.None,
                (_, __, ___) => Task.CompletedTask,
                (sub, _, __) =>
                {
                    tcs.SetResult(sub);
                });

            subscription.Dispose();
            var receivedSubscription = await tcs.Task.WithTimeout();

            receivedSubscription.ShouldBe(subscription);
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task When_stream_subscription_disposed_while_handling_messages_then_should_drop_subscription_with_reason_Disposed()
        {
            string streamId = "stream-1";
            var droppedTcs = new TaskCompletionSource<SubscriptionDroppedReason>();
            var handler = new AsyncAutoResetEvent();
            var subscription = Store.SubscribeToStream(streamId,
                StreamVersion.Start,
                async (_, __, ct) =>
                {
                    handler.Set();
                    await Task.Delay(TimeSpan.FromSeconds(3)); // block "handling" while a dispose occurs
                },
                (_, reason, __) =>
                {
                    droppedTcs.SetResult(reason);
                });
            // First message is blocked in handling, the second is co-operatively cancelled
            await AppendMessages(Store, streamId, 2); 
            await handler.WaitAsync().WithTimeout(10000);
            subscription.Dispose();

            var droppedReason = await droppedTcs.Task.WithTimeout(10000);

            droppedReason.ShouldBe(SubscriptionDroppedReason.Disposed);
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task Can_dispose_stream_subscription_multiple_times()
        {
            var streamId = "stream-1";
            var subscription = Store.SubscribeToStream(streamId,
                StreamVersion.Start,
                (_, __, ___) => Task.CompletedTask);
            await AppendMessages(Store, streamId, 2);
            subscription.Dispose();
            subscription.Dispose();
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task When_exception_throw_by_all_stream_subscriber_then_should_drop_subscription_with_reason_SubscriberError()
        {
            var eventReceivedException = new TaskCompletionSource<SubscriptionDroppedReason>();
            Task MessageReceived(IAllStreamSubscription _, StreamMessage __, CancellationToken ___) => throw new Exception();
            void SubscriptionDropped(IAllStreamSubscription _, SubscriptionDroppedReason reason, Exception __) => eventReceivedException.SetResult(reason);
            var streamId = "stream-1";

            using (Store.SubscribeToAll(
                Position.None,
                MessageReceived,
                SubscriptionDropped))
            {
                await Store.AppendToStream(
                    streamId,
                    ExpectedVersion.NoStream,
                    new NewStreamMessage(Guid.NewGuid(), "type", "{}"));

                var droppedReason = await eventReceivedException.Task.WithTimeout();

                droppedReason.ShouldBe(SubscriptionDroppedReason.SubscriberError);
            }
        }

        [Fact(Skip = "Race condition in CI. To investigate DH"), Trait("Category", "Subscriptions")]
        public async Task When_all_stream_subscription_disposed_then_should_drop_subscription_with_reason_Disposed()
        {
            var tcs = new TaskCompletionSource<SubscriptionDroppedReason>();
            var subscription = Store.SubscribeToAll(
                Position.End,
                (_, __, ___) => Task.CompletedTask,
                (_, reason, __) =>
                {
                    tcs.SetResult(reason);
                });
            subscription.Dispose();
            var droppedReason = await tcs.Task.WithTimeout();

            droppedReason.ShouldBe(SubscriptionDroppedReason.Disposed);
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task When_all_stream_subscription_dropped_then_should_supply_subscription_instance()
        {
            var tcs = new TaskCompletionSource<IAllStreamSubscription>();
            var subscription = Store.SubscribeToAll(
                Position.End,
                (_, __, ___) => Task.CompletedTask,
                (sub, _, __) =>
                {
                    tcs.SetResult(sub);
                });
            subscription.Dispose();
            var receivedSubscription = await tcs.Task.WithTimeout();

            receivedSubscription.ShouldBe(subscription);
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task When_all_stream_subscription_disposed_while_handling_messages_then_should_drop_subscription_with_reason_Disposed()
        {
            string streamId = "stream-1";
            var droppedTcs = new TaskCompletionSource<SubscriptionDroppedReason>();
            var handler = new AsyncAutoResetEvent();
            var subscription = Store.SubscribeToAll(
                Position.End,
                async (_, __, ___) =>
                {
                    handler.Set();
                    await Task.Delay(TimeSpan.FromSeconds(3)); // block "handling" while a dispose occurs
                },
                (_, reason, __) =>
                {
                    droppedTcs.SetResult(reason);
                });
            // First message is blocked in handling, the second is cooperatively cancelled
            await subscription.Started;
            await AppendMessages(Store, streamId, 2);
            await handler.WaitAsync().WithTimeout();
            subscription.Dispose();

            var droppedReason = await droppedTcs.Task.WithTimeout();

            droppedReason.ShouldBe(SubscriptionDroppedReason.Disposed);
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task Can_dispose_all_stream_subscription_multiple_times()
        {
            var streamId = "stream-1";
            var subscription = Store.SubscribeToAll(
                Position.Start,
                (_, __, ___) => Task.CompletedTask);
            await AppendMessages(Store, streamId, 2);
            subscription.Dispose();
            subscription.Dispose();
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task When_caught_up_to_all_then_then_should_notify()
        {
            var streamId = "stream-1";
            await AppendMessages(Store, streamId, 30);
            var caughtUp = new TaskCompletionSource<bool>();
            var subscription = Store.SubscribeToAll(
                Position.None,
                (_, __, ___) => Task.CompletedTask,
                hasCaughtUp: b =>
                {
                    if(b)
                    {
                        caughtUp.SetResult(b);
                    }
                });
            subscription.MaxCountPerRead = 10;
            await caughtUp.Task.WithTimeout(5000);
            subscription.Dispose();
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task When_caught_up_to_all_then_then_should_notify_only_twice()
        {
            var streamId = "stream-1";
            await AppendMessages(Store, streamId, 30);
            var caughtUp = new TaskCompletionSource<bool>();
            var numberOfCaughtUps = 0;
            var subscription = Store.SubscribeToAll(
                Position.None,
                (_, __, ___) => Task.CompletedTask,
                hasCaughtUp: b =>
                {
                    if(b)
                    {
                        if(++numberOfCaughtUps > 2)
                        {
                            caughtUp.SetException(
                                new Exception("Should not raise hasCaughtUp more than twice."));
                        }
                    }
                });
            subscription.MaxCountPerRead = 10;

            Func<Task> act = async () => await caughtUp.Task.WithTimeout(1000);

            await act.ShouldThrowAsync<TimeoutException>();

            subscription.Dispose();
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task When_caughtup_to_stream_then_then_should_notify()
        {
            var streamId = "stream-1";
            await AppendMessages(Store, streamId, 30);
            var caughtUp = new TaskCompletionSource<bool>();
            var subscription = Store.SubscribeToStream(
                streamId,
                StreamVersion.None,
                (_, __, ___) => Task.CompletedTask,
                hasCaughtUp: b =>
                {
                    if (b)
                    {
                        caughtUp.SetResult(b);
                    }
                });
            subscription.MaxCountPerRead = 10;
            await caughtUp.Task.WithTimeout(5000);
            subscription.Dispose();
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task When_falls_behind_on_all_then_then_should_notify()
        {
            var streamId = "stream-1";
            await AppendMessages(Store, streamId, 30);
            var fallenBehind = new TaskCompletionSource<bool>();
            bool caughtUp = false;

            var subscription = Store.SubscribeToAll(
                Position.None,
                (_, __, ___) => Task.CompletedTask,
                hasCaughtUp: b =>
                {
                    if (b)
                    {
                        caughtUp = true;
                    }
                    if (b && caughtUp)
                    {
                        fallenBehind.SetResult(b);
                    }
                });
            subscription.MaxCountPerRead = 10;

            await fallenBehind.Task.WithTimeout(5000);
            subscription.Dispose();
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task When_falls_behind_on_stream_then_then_should_notify()
        {
            var streamId = "stream-1";
            await AppendMessages(Store, streamId, 30);
            var fallenBehind = new TaskCompletionSource<bool>();
            bool caughtUp = false;

            var subscription = Store.SubscribeToStream(
                streamId,
                StreamVersion.None,
                (_, __, ___) => Task.CompletedTask,
                hasCaughtUp: b =>
                {
                    if (b)
                    {
                        caughtUp = true;
                    }
                    if(b && caughtUp)
                    {
                        fallenBehind.SetResult(b);
                    }
                });
            subscription.MaxCountPerRead = 10;

            await fallenBehind.Task.WithTimeout(5000);
            subscription.Dispose();
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task When_dispose_store_then_should_dispose_stream_subscriptions()
        {
            var subscriptionDropped = new TaskCompletionSource<SubscriptionDroppedReason>();
            var subscription = Store.SubscribeToStream(
                "stream-1",
                StreamVersion.None,
                (_, __, ___) => Task.CompletedTask,
                subscriptionDropped: (streamSubscription, reason, exception) =>
                {
                    subscriptionDropped.SetResult(reason);
                });

            Store.Dispose();

            var droppedReason = await subscriptionDropped.Task.WithTimeout(5000);

            droppedReason.ShouldBe(SubscriptionDroppedReason.Disposed);
        }

        [Fact(Skip="Race condition. Disabling to get green CI. To be addressed. DH"), Trait("Category", "Subscriptions")]
        public async Task When_dispose_store_then_should_dispose_all_stream_subscriptions()
        {
            var subscriptionDropped = new TaskCompletionSource<SubscriptionDroppedReason>();
            var subscription = Store.SubscribeToAll(
                Position.None,
                (_, __, ___) => Task.CompletedTask,
                subscriptionDropped: (streamSubscription, reason, exception) =>
                {
                    subscriptionDropped.SetResult(reason);
                });

            Store.Dispose();

            var droppedReason = await subscriptionDropped.Task.WithTimeout(5000);

            droppedReason.ShouldBe(SubscriptionDroppedReason.Disposed);
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task When_subscribe_to_stream_and_append_messages_then_should_receive_message()
        {
            var streamId1 = "stream-1";
            var streamId2 = "stream-2";
            var received = new AsyncAutoResetEvent();
            string streamIdReceived = null;

            using (Store.SubscribeToStream(
                streamId1,
                StreamVersion.None,
                (_, message, ct) =>
                {
                    streamIdReceived = message.StreamId;
                    received.Set();
                    return Task.CompletedTask;
                }))
            {
                await AppendMessages(Store, streamId1, 1);
                await AppendMessages(Store, streamId2, 1);
                await received.WaitAsync().WithTimeout();
                streamIdReceived.ShouldBe(streamId1);

                await AppendMessages(Store, streamId1, 1);
                await AppendMessages(Store, streamId2, 1);
                await received.WaitAsync().WithTimeout();
                streamIdReceived.ShouldBe(streamId1);

                await AppendMessages(Store, streamId1, 1);
                await AppendMessages(Store, streamId2, 1);
                await received.WaitAsync().WithTimeout();
                streamIdReceived.ShouldBe(streamId1);
            }
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task When_subscribe_to_all_with_empty_store_should_raise_has_caught_up()
        {
            bool hasCaughtUp;
            var tcs = new TaskCompletionSource<bool>();
            using (Store.SubscribeToAll(
                null,
                (_, message, ct) => Task.CompletedTask,
                hasCaughtUp: b => tcs.SetResult(b)))
            {
                hasCaughtUp = await tcs.Task.WithTimeout(5000);
            }
            hasCaughtUp.ShouldBeTrue();
        }

        [Fact, Trait("Category", "Subscriptions")]
        public async Task When_subscribe_to_stream_with_empty_store_should_raise_has_caught_up()
        {
            bool hasCaughtUp;
            var tcs = new TaskCompletionSource<bool>();
            using (Store.SubscribeToStream(
                "stream-1",
                null,
                (_, message, ct) => Task.CompletedTask,
                hasCaughtUp: b => tcs.SetResult(b)))
            {
                hasCaughtUp = await tcs.Task.WithTimeout(5000);
            }
            hasCaughtUp.ShouldBeTrue();
        }

        private static async Task AppendMessages(
            IStreamStore streamStore,
            string streamId,
            int numberOfEvents)
        {
            for(int i = 0; i < numberOfEvents; i++)
            {
                var newmessage = new NewStreamMessage(Guid.NewGuid(), "MyEvent", "{}");
                await streamStore.AppendToStream(streamId, ExpectedVersion.Any, newmessage);
            }
        }
    }
}
