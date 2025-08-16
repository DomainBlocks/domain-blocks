using MongoDB.Bson;

namespace DomainBlocks.Persistence.Mongo.Events;

internal static class BsonDocumentExtensions
{
    public static void SetValueByPath(this BsonDocument root, string dottedPath, BsonValue value)
    {
        var parts = dottedPath.Split('.');
        var current = root;

        for (var i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i];

            if (!current.TryGetValue(part, out var nested) || nested.BsonType != BsonType.Document)
            {
                var newDoc = new BsonDocument();
                current[part] = newDoc;
                current = newDoc;
            }
            else
            {
                current = nested.AsBsonDocument;
            }
        }

        current[parts[^1]] = value;
    }

    public static BsonValue GetValueByPath(this BsonDocument document, string path)
    {
        var parts = path.Split('.');
        BsonValue current = document;

        foreach (var part in parts)
        {
            if (current is BsonDocument doc && doc.TryGetValue(part, out var next))
            {
                current = next;
            }
            else
            {
                throw new KeyNotFoundException($"Element at path '{path}' not found.");
            }
        }

        return current;
    }
}