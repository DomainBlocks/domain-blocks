using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Shopping.Domain.Aggregates;

namespace DomainBlocks.IntegrationTests.Experimental;

public class MutableShoppingCartJsonConverter : JsonConverter<MutableShoppingCart>
{
    public override MutableShoppingCart Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var root = JsonNode.Parse(ref reader)!;

        var id = root["Id"].Deserialize<Guid>(options);
        var items = root["Items"].Deserialize<List<ShoppingCartItem>>(options);

        return new MutableShoppingCart(id, items);
    }

    public override void Write(Utf8JsonWriter writer, MutableShoppingCart value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value);
    }
}