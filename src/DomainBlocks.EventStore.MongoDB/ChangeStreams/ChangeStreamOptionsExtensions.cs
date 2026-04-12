using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

internal static class ChangeStreamOptionsExtensions
{
    extension(ChangeStreamOptions options)
    {
        public ChangeStreamOptions Copy()
        {
            return new ChangeStreamOptions
            {
                BatchSize = options.BatchSize,
                Collation = options.Collation,
                Comment = options.Comment,
                FullDocument = options.FullDocument,
                FullDocumentBeforeChange = options.FullDocumentBeforeChange,
                MaxAwaitTime = options.MaxAwaitTime,
                ResumeAfter = options.ResumeAfter,
                ShowExpandedEvents = options.ShowExpandedEvents,
                StartAfter = options.StartAfter,
                StartAtOperationTime = options.StartAtOperationTime
            };
        }
    }
}