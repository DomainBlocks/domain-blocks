using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shopping.ReadModel.Db;
using Shopping.ReadModel.Db.Model;

namespace Shopping.ReadModel.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ShoppingCartController : ControllerBase
    {
        private readonly ShoppingCartDbContext _dbContext;

        public ShoppingCartController(ShoppingCartDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<IEnumerable<ShoppingCartSummaryItem>> Get()
        {
            return await _dbContext.ShoppingCartSummaryItems.ToListAsync();
        }
    }
}
