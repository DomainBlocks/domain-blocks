using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Shopping.ReadModel;

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
        var cartIds = await _dbContext.ShoppingCartSummaryItems.AsNoTracking()
            .Select(i => i.SessionId).Distinct()
            .ToListAsync();

        return Ok(cartIds);
    }

    [HttpGet("{sessionId:guid}/items")]
    public async Task<ActionResult<IEnumerable<ShoppingCartSummaryItem>>> GetItemsInCart(Guid sessionId)
    {
        var items = await _dbContext.ShoppingCartSummaryItems
            .AsNoTracking()
            .Where(c => c.SessionId == sessionId)
            .ToListAsync();

        return Ok(items);
    }
}