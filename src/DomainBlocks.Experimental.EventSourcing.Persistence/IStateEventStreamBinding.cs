namespace DomainBlocks.Experimental.EventSourcing.Persistence;

internal interface IStateEventStreamBinding
{
    Type StateType { get; }
}