using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shopping.ReadModel.Db;
using Shopping.ReadModel.Model;

namespace Shopping.ReadModel.Controllers;

[ApiController]
[Route("ShoppingCart")]
public class ShoppingCartReadModelController : ControllerBase
{
    private readonly ShoppingCartDbContext _dbContext;

    public ShoppingCartReadModelController(ShoppingCartDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("sessionIds")]
    public async Task<ActionResult<IEnumerable<Guid>>> GetCartIds()
    {
        var cartIds = await _dbContext.ShoppingCarts
            .AsNoTracking()
            .Select(i => i.SessionId).Distinct()
            .ToListAsync();

        return Ok(cartIds);
    }

    [HttpGet("{sessionId:guid}/items")]
    public async Task<ActionResult<IEnumerable<ShoppingCartItem>>> GetItemsInCart(Guid sessionId)
    {
        var items = await _dbContext.ShoppingCarts
            .AsNoTracking()
            .Where(c => c.SessionId == sessionId)
            .SelectMany(c => c.Items)
            .ToListAsync();

        return Ok(items);
    }

    [HttpGet("summaries")]
    public async Task<ActionResult<IEnumerable<Guid>>> GetSummaries()
    {
        var cartIds = await _dbContext.ShoppingCartSummaries
            .AsNoTracking()
            .Select(x => new { x.SessionId, x.ItemCount })
            .ToListAsync();

        return Ok(cartIds);
    }
}