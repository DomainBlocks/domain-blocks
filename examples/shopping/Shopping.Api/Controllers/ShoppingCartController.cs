using DomainBlocks.Persistence;
using Microsoft.AspNetCore.Mvc;
using Shopping.Domain;

namespace Shopping.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class ShoppingCartController : ControllerBase
{
    private readonly IEntityStore _entityStore;

    public ShoppingCartController(IEntityStore entityStore)
    {
        _entityStore = entityStore;
    }

    [HttpPost]
    public async Task<ActionResult<string>> StartShoppingSession(CancellationToken cancellationToken)
    {
        var shoppingCart = new ShoppingCart();
        shoppingCart.StartSession();
        await _entityStore.SaveAsync(shoppingCart, cancellationToken);
        return Ok(shoppingCart.Id);
    }

    [HttpPost("{sessionId}/items")]
    public async Task<ActionResult> AddItemToShoppingCart(
        string sessionId, [FromBody] string item, CancellationToken cancellationToken)
    {
        var shoppingCart = await _entityStore.LoadAsync<ShoppingCart>(sessionId, cancellationToken);
        shoppingCart.AddItem(item);
        await _entityStore.SaveAsync(shoppingCart, cancellationToken);
        return Ok();
    }

    [HttpDelete("{sessionId}/items/{item}")]
    public async Task<ActionResult> RemoveItemFromShoppingCart(
        string sessionId, string item, CancellationToken cancellationToken)
    {
        var shoppingCart = await _entityStore.LoadAsync<ShoppingCart>(sessionId, cancellationToken);
        shoppingCart.RemoveItem(item);
        await _entityStore.SaveAsync(shoppingCart, cancellationToken);
        return Ok();
    }
}