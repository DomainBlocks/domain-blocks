using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using EventStore.Client;
using NUnit.Framework;

namespace DomainBlocks.Projections.Sql.Tests.Fakes
{
    public class FakeJsonEventPublisher : IEventPublisher<EventRecord>
    {
        private Func<EventNotification<EventRecord>, Task> _onEvent;
        public bool IsStarted { get; private set; }

        public Task StartAsync(Func<EventNotification<EventRecord>, Task> onEvent)
        {
            _onEvent = onEvent;
            IsStarted = true;
            return Task.CompletedTask;
        }

        public void Stop()
        {
            IsStarted = false;
        }

        public async Task SendEvent(object @event, string eventType, Guid? eventId = null)
        {
            AssertPublisherStarted();
            var data = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(@event)));
            var eventRecord = new EventRecord("dummyStreamId",
                                              Uuid.FromGuid(eventId ?? Guid.NewGuid()),
                                              StreamPosition.Start,
                                              Position.Start,
                                              new Dictionary<string, string>(),
                                              data,
                                              null);

            await _onEvent(EventNotification.FromEvent(eventRecord, eventRecord.EventType, eventRecord.EventId.ToGuid()));
        }

        public async Task SendCaughtUp()
        {
            AssertPublisherStarted();
            await _onEvent(EventNotification.CaughtUp<EventRecord>());
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