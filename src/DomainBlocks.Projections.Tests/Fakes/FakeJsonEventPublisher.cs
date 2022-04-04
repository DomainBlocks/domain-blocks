using System;
using System.Collections.Generic;
using System.Net.Mime;
using System.Threading.Tasks;
using DomainBlocks.Serialization;
using NUnit.Framework;

namespace DomainBlocks.Projections.Sql.Tests.Fakes
{
    public class FakeJsonEventPublisher : IEventPublisher<object>
    {
        private Func<EventNotification<object>, Task> _onEvent;
        public bool IsStarted { get; private set; }

        public Task StartAsync(Func<EventNotification<object>, Task> onEvent)
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
            var eventMetadata = new Dictionary<string, string>
            {
                { "type", eventType },
                { "created", DateTimeOffset.UtcNow.Ticks.ToString() },
                { "content-type", MediaTypeNames.Application.Json },
            };

            var readEvents = new List<ReadEvent<object>>
            {
                new(eventId ?? Guid.NewGuid(), @event, EventMetadata.FromKeyValuePairs(eventMetadata), eventType)
            };

            await _onEvent(EventNotification.FromEvents(readEvents));
        }

        public async Task SendCaughtUp()
        {
            AssertPublisherStarted();
            await _onEvent(EventNotification.CaughtUp<object>());
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