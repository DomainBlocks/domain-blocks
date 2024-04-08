using DomainBlocks.V1.Subscriptions;
using Microsoft.AspNetCore.Mvc;

namespace Shopping.ReadModel;

[ApiController]
[Route("Subscriptions")]
public class SubscriptionsController : ControllerBase
{
    private readonly Dictionary<string, EventStreamSubscriptionService> _subscriptions;

    public SubscriptionsController(IEnumerable<EventStreamSubscriptionService> subscriptions)
    {
        _subscriptions = subscriptions.ToDictionary(x => x.Name);
    }

    [HttpGet]
    public ActionResult<IEnumerable<SubscriptionDto>> GetSubscriptions()
    {
        var results = _subscriptions.Values
            .Select(x => new SubscriptionDto
            {
                Name = x.Name,
                Consumers = x.ConsumerSessions
                    .Select(c => new ConsumerDto
                    {
                        Name = c.ConsumerName,
                        Status = c.Status.ToString(),
                        Error = c.Error?.Message
                    })
                    .ToArray()
            });

        return Ok(results);
    }
    
    public class SubscriptionDto
    {
        public string Name { get; set; } = null!;
        public ConsumerDto[] Consumers { get; set; } = null!;
    }

    public class ConsumerDto
    {
        public string Name { get; set; } = null!;
        public string Status { get; set; } = null!;
        public string? Error { get; set; }
    }
}