namespace DomainBlocks.EventStore;

/// <summary>
/// Reports that the subscriber consumed events too slowly, or the store lost its live feed, and events may have been
/// skipped. The subscription resumes from the last delivered position and reports <see cref="SubscriptionCaughtUp"/>
/// once it has caught up again. A case of <see cref="SubscriptionMessage{TEvent,TStreamId,TStreamPos,TLogPos}"/>.
/// </summary>
public readonly record struct SubscriptionFellBehind;