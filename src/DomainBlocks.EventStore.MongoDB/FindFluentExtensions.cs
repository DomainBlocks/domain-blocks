using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

internal static class FindFluentExtensions
{
    extension(IFindFluent<BsonDocument, BsonDocument> find)
    {
        /// <summary>
        /// Leaves the metadata field out of the documents returned when the caller does not want it.
        /// </summary>
        public IFindFluent<BsonDocument, BsonDocument> SetExcludeMetadata(bool isExcluded)
        {
            return isExcluded
                ? find.Project<BsonDocument>(
                    Builders<BsonDocument>.Projection.Exclude(EventLogEntry.FieldNames.Metadata))
                : find;
        }
    }
}