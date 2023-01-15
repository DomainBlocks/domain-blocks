using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shopping.ReadModel.Db;
using Shopping.ReadModel.Db.Model;

namespace Shopping.ReadModel.Controllers;

[ApiController]
public class ShoppingCartController : ControllerBase
{
    private readonly ShoppingCartDbContext _dbContext;

    public ShoppingCartController(ShoppingCartDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("cartIds")]
    public async Task<ActionResult<IEnumerable<Guid>>> GetCartIds()
    {
        var cartIds = await _dbContext.ShoppingCartSummaryItems.AsNoTracking().Select(i => i.CartId).Distinct()
            .ToListAsync();
        return Ok(cartIds);
    }

    [HttpGet("cartItems/{cartId}")]
    public async Task<ActionResult<IEnumerable<ShoppingCartSummaryItem>>> GetItemsInCart(Guid cartId)
    {
        var items = await _dbContext.ShoppingCartSummaryItems.AsNoTracking()
            .Where(c => c.CartId == cartId)
            .ToListAsync();

        return Ok(items);
    }
}