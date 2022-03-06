using System;

namespace Shopping.ReadModel.Db.Model
{
    public class ShoppingCartHistory
    {
        public int Id { get; set; }

        public Guid CartId { get; set; }

        public string EventName { get; set; }
    }
}