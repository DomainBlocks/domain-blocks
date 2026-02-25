using System.Linq.Expressions;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.Utilities;

public static class FieldPathResolver
{
    public static string Resolve<TDocument, TField>(Expression<Func<TDocument, TField>> expression)
    {
        var field = new ExpressionFieldDefinition<TDocument, TField>(expression);
        var serializer = BsonSerializer.SerializerRegistry.GetSerializer<TDocument>();
        var args = new RenderArgs<TDocument>(serializer, BsonSerializer.SerializerRegistry);
        return field.Render(args).FieldName;
    }
}