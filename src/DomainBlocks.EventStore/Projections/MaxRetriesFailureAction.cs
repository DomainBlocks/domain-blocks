namespace DomainBlocks.EventStore.Projections;

public enum MaxRetriesFailureAction
{
    // Park the event in the EventStore failure queue
    Park,
    // Skip the event and do not process again
    Skip
}