using System;

namespace Shopping.Domain.Events.New;

public record ShoppingSessionStarted(Guid SessionId) : IDomainEvent;