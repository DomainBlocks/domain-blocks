using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Shopping.ReadModel.Db;

namespace Shopping.ReadModel
{
    public class ReadModelDbInitializer
    {
        private readonly DbContext dbContext;

        public ReadModelDbInitializer(ShoppingCartDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        public async Task InitializeDb()
        {
            await dbContext.Database.EnsureCreatedAsync();
        }
    }
}