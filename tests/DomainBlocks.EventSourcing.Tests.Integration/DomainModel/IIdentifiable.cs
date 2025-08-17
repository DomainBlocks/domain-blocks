namespace DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

public interface IIdentifiable
{
    Guid Id { get; }
}