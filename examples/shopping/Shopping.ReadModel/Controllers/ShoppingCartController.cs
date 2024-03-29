using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shopping.ReadModel.Db;
using Shopping.ReadModel.Db.Model;

namespace Shopping.ReadModel.Controllers;

[ApiController]
[Route("[controller]")]
public class ShoppingCartController : ControllerBase
{
    private readonly ShoppingCartDbContext _dbContext;

    public ShoppingCartController(ShoppingCartDbContext dbContext)
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