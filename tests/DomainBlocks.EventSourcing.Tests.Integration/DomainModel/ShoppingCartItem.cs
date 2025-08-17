namespace DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

public record ShoppingCartItem(Guid SessionId, string Name);