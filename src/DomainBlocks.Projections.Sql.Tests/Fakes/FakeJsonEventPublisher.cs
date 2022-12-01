using System;
using System.Collections.Generic;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Projections.New;
using EventStore.Client;
using NUnit.Framework;
using DomainBlocksStreamPosition = DomainBlocks.Projections.New.StreamPosition;
using StreamPosition = EventStore.Client.StreamPosition;

namespace DomainBlocks.Projections.Sql.Tests.Fakes
{
    public class FakeJsonEventPublisher : IEventPublisher<EventRecord>
    {
        private Func<EventNotification<EventRecord>, CancellationToken, Task> _onEvent;
        public bool IsStarted { get; private set; }

        public Task StartAsync(
            Func<EventNotification<EventRecord>, CancellationToken, Task> onEvent,
            IStreamPosition position = null,
            CancellationToken cancellationToken = default)
        {
            _onEvent = onEvent;
            IsStarted = true;
            return Task.CompletedTask;
        }

        public void Stop()
        {
            IsStarted = false;
        }

        public async Task SendEvent(
            object @event,
            string eventType,
            Guid? eventId = null,
            CancellationToken cancellationToken = default)
        {
            AssertPublisherStarted();
            var data = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(@event)));
            var eventMetadata = new Dictionary<string, string>
            {
                { "type", eventType },
                { "created", DateTimeOffset.UtcNow.Ticks.ToString() },
                { "content-type", MediaTypeNames.Application.Json },
            };

            var eventRecord = new EventRecord("dummyStreamId",
                Uuid.FromGuid(eventId ?? Guid.NewGuid()),
                StreamPosition.Start,
                Position.Start,
                eventMetadata,
                data,
                null);

            var notification = EventNotification.FromEvent(
                eventRecord, eventRecord.EventType, eventRecord.EventId.ToGuid(), DomainBlocksStreamPosition.Empty);

            await _onEvent(notification, cancellationToken);
        }

        public async Task SendCaughtUp(CancellationToken cancellationToken = default)
        {
            AssertPublisherStarted();
            var notification = EventNotification.CaughtUp<EventRecord>(DomainBlocksStreamPosition.Empty);
            await _onEvent(notification, cancellationToken);
        }

        private void AssertPublisherStarted()
        {
            if (_onEvent == null)
            {
                Assert.Fail("Publisher has not been started");
            }
        }
    }
}