using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DomainLib.Projections.Sql.Tests.Fakes
{
    public class FakeJsonEventPublisher : IEventPublisher<byte[]>
    {
        private Func<EventNotification<byte[]>, Task> _onEvent;
        public bool IsStarted { get; private set; }

        public Task StartAsync(Func<EventNotification<byte[]>, Task> onEvent)
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
            var byteArray = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(@event));
            await _onEvent(EventNotification.FromEvent(byteArray, eventType, eventId ?? Guid.NewGuid()));
        }

        public async Task SendCaughtUp()
        {
            AssertPublisherStarted();
            await _onEvent(EventNotification.CaughtUp<byte[]>());
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