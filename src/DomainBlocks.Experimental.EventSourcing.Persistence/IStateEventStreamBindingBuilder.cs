namespace DomainBlocks.Experimental.EventSourcing.Persistence;

internal interface IStateEventStreamBindingBuilder
{
    IStateEventStreamBinding Build();
}