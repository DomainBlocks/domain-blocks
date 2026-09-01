using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventSourcing;

public sealed class StateNotFoundException(object stateId) :
    DomainBlocksException($"State with ID '{stateId}' not found.")
{
    public object StateId { get; } = stateId;
}