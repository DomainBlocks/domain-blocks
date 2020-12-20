using Dapper;
using DomainLib.Aggregates;
using DomainLib.Common;
using DomainLib.Projections.Sql;
using DomainLib.Projections.Sql.Tests.Fakes;
using DomainLib.Projections.Sqlite.Tests.Events;
using DomainLib.Serialization.Json;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
// ReSharper disable UnusedAutoPropertyAccessor.Local
// ReSharper disable ClassNeverInstantiated.Local

namespace DomainLib.Projections.Sqlite.Tests
{
    [TestFixture]
    public class InMemoryDbTests
    {
        private FakeJsonEventPublisher _publisher;
        private EventDispatcher<object> _eventDispatcher;
        private IDbConnection _connection;
        private SqliteDbConnector _dbConnector;

        private class ShoppingCartItemRow
        {
            public string Id { get; set; }
            public string CartId { get; set; }
            public string ItemDescription { get; set; }
            public decimal ItemCost { get; set; }
        }

        private class ShoppingCartSummaryRow
        {
            public string CartId { get; set; }
            public int ItemCount { get; set; }
            public decimal TotalCost { get; set; }
        }

        [Test]
        public async Task SqlSchemaIsBuiltAndPopulatedCorrectly()
        {
            await _eventDispatcher.StartAsync();
            _connection = _dbConnector.Connection;

            await _publisher.SendCaughtUp();
            var cartId = Guid.NewGuid();
            await _publisher.SendEvent(new ShoppingCartCreated(cartId), ShoppingCartCreated.EventName);

            var item1Id = Guid.NewGuid();
            var item2Id = Guid.NewGuid();
            var item3Id = Guid.NewGuid();
            var item1Cost = 12.5m;
            var item2Cost = 25m;
            var item3Cost = 4.99m;
            await _publisher.SendEvent(new ItemAddedToShoppingCart(cartId, item1Id, "Item1", item1Cost), ItemAddedToShoppingCart.EventName);
            await _publisher.SendEvent(new ItemAddedToShoppingCart(cartId, item2Id, "Item2", item2Cost), ItemAddedToShoppingCart.EventName);
            await _publisher.SendEvent(new ItemAddedToShoppingCart(cartId, item3Id, "Item3", item3Cost), ItemAddedToShoppingCart.EventName);

            var items = await ExecuteQuery<ShoppingCartItemRow>("SELECT * FROM ShoppingCartItems");
            var summary = (await ExecuteQuery<ShoppingCartSummaryRow>("SELECT * FROM ShoppingCartSummary")).Single();

            Assert.That(items.Count, Is.EqualTo(3));
            Assert.That(items[0].CartId, Is.EqualTo(cartId.ToString()));
            Assert.That(items[0].ItemDescription, Is.EqualTo("Item1"));
            Assert.That(items[0].ItemCost, Is.EqualTo(item1Cost));
            Assert.That(items[0].Id, Is.EqualTo(item1Id.ToString()));

            Assert.That(items[1].CartId, Is.EqualTo(cartId.ToString()));
            Assert.That(items[1].ItemDescription, Is.EqualTo("Item2"));
            Assert.That(items[1].ItemCost, Is.EqualTo(item2Cost));
            Assert.That(items[1].Id, Is.EqualTo(item2Id.ToString()));

            Assert.That(items[2].CartId, Is.EqualTo(cartId.ToString()));
            Assert.That(items[2].ItemDescription, Is.EqualTo("Item3"));
            Assert.That(items[2].ItemCost, Is.EqualTo(item3Cost));
            Assert.That(items[2].Id, Is.EqualTo(item3Id.ToString()));

            Assert.That(summary.CartId, Is.EqualTo(cartId.ToString()));
            Assert.That(summary.ItemCount, Is.EqualTo(3));
            Assert.That(summary.TotalCost, Is.EqualTo(item1Cost + item2Cost + item3Cost));
        }

        private async Task<List<T>> ExecuteQuery<T>(string sql)
        {
            var command = _connection.CreateCommand();
            command.CommandText = sql;

            var results = await _connection.QueryAsync<T>(sql);

            return results.ToList();
        }

        [SetUp]
        public void SetUp()
        {
            var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace)
                                                                       .AddConsole());

            Logger.SetLoggerFactory(loggerFactory);
            var projectionRegistryBuilder = new ProjectionRegistryBuilder();

            _dbConnector = new SqliteDbConnector("Data Source=:memory:;Version=3;Pooling=True;Max Pool Size=100;");

            ShoppingCartSummarySqlProjection.Register(projectionRegistryBuilder, _dbConnector);
            ShoppingCartItemsSqlProjection.Register(projectionRegistryBuilder, _dbConnector);

            var registry = projectionRegistryBuilder.Build();

            // TODO: EventNameMap isn't actually used here as we deserialize differently on the read side.
            // We need to fix this so that we don't have to pass an empty instance in
            var serializer = new JsonEventSerializer(new EventNameMap());
            var eventPublisher = new FakeJsonEventPublisher();
            _publisher = eventPublisher;

            var eventDispatcher = new EventDispatcher<object>(eventPublisher,
                                                              registry.EventProjectionMap,
                                                              registry.EventContextMap,
                                                              serializer,
                                                              registry.EventNameMap,
                                                              EventDispatcherConfiguration.ReadModelDefaults with {
                                                                  ProjectionHandlerTimeout = TimeSpan.FromMinutes(5)});
            _eventDispatcher = eventDispatcher;
        }

        private class ShoppingCartItemsSqlProjection : ISqlProjection
        {
            public static void Register(ProjectionRegistryBuilder builder, SqliteDbConnector dbConnector)
            {
                var shoppingCartItems = new ShoppingCartItemsSqlProjection(dbConnector);

                builder.Event<ItemAddedToShoppingCart>()
                       .FromName(ItemAddedToShoppingCart.EventName)
                       .ToSqlProjection(shoppingCartItems)
                       .ExecutesUpsert();

                builder.Event<ItemRemovedFromShoppingCart>()
                       .FromName(ItemRemovedFromShoppingCart.EventName)
                       .ToSqlProjection(shoppingCartItems)
                       .ExecutesDelete();
            }

            public ShoppingCartItemsSqlProjection(IDbConnector dbConnector)
            {
                DbConnector = dbConnector;
            }

            public IDbConnector DbConnector { get; }
            public ISqlDialect SqlDialect { get; } = new SqliteSqlDialect();
            public string TableName { get; } = "ShoppingCartItems";

            public SqlColumnDefinitions Columns { get; } = new()
            {
                { nameof(ItemAddedToShoppingCart.ItemId), new SqlColumnDefinitionBuilder().Name("Id").Type(DbType.String).PrimaryKey().Build() },
                { nameof(ItemAddedToShoppingCart.CartId), new SqlColumnDefinitionBuilder().Name("CartId").Type(DbType.String).NotNull().Build() },
                { nameof(ItemAddedToShoppingCart.ItemDescription), new SqlColumnDefinitionBuilder().Name("ItemDescription").Type(DbType.String).NotNull().Build() },
                { nameof(ItemAddedToShoppingCart.ItemCost), new SqlColumnDefinitionBuilder().Name("ItemCost").Type(DbType.Decimal).NotNull().Build() },
            };
        }

        private class ShoppingCartSummarySqlProjection : ISqlProjection
        {
            public static void Register(ProjectionRegistryBuilder builder, SqliteDbConnector dbConnector)
            {
                var shoppingCartSummary = new ShoppingCartSummarySqlProjection(dbConnector);

                builder.Event<ShoppingCartCreated>()
                       .FromName(ShoppingCartCreated.EventName)
                       .ToSqlProjection(shoppingCartSummary)
                       .ParameterMappings((nameof(ItemAddedToShoppingCart.CartId), e => e.Id))
                       .ExecutesUpsert();

                builder.Event<ItemAddedToShoppingCart>()
                       .FromName(ItemAddedToShoppingCart.EventName)
                       .ToSqlProjection(shoppingCartSummary)
                       .ParameterMappings(("ItemCost", e => e.ItemCost))
                       .ExecutesCustomSql(@"
UPDATE ShoppingCartSummary
SET ItemCount = ItemCount + 1,
    TotalCost = TotalCost + @ItemCost
");

                builder.Event<ItemRemovedFromShoppingCart>()
                       .FromName(ItemRemovedFromShoppingCart.EventName)
                       .ToSqlProjection(shoppingCartSummary)
                       // This probably isn't how we'd do it in the real world as 
                       // the SQL here relies on the fact that it runs before the command
                       // to delete the item from ShoppingCartItems.
                       // It demonstrates the point that arbitrary SQL can be run at least
                       .ExecutesCustomSql(@"
UPDATE ShoppingCartSummary
SET ItemCount = ItemCount - 1,
    TotalCost = TotalCost - (SELECT ItemCost
                             FROM ShoppingCartItems
                             WHERE CartId = @CartId
                             AND ItemId = @ItemId
                             );
");
            }

            public ShoppingCartSummarySqlProjection(IDbConnector dbConnector)
            {
                DbConnector = dbConnector;
            }

            public IDbConnector DbConnector { get; }
            public ISqlDialect SqlDialect { get; } = new SqliteSqlDialect();
            public string TableName { get; } = "ShoppingCartSummary";

            public SqlColumnDefinitions Columns { get; } = new()
            {
                { nameof(ItemAddedToShoppingCart.CartId), new SqlColumnDefinitionBuilder().Name("CartId").Type(DbType.String).PrimaryKey().Build() },
                { "ItemCount", new SqlColumnDefinitionBuilder().Name("ItemCount").Type(DbType.Int32).Default(0).Build() },
                { "TotalCost", new SqlColumnDefinitionBuilder().Name("TotalCost").Type(DbType.Decimal).Default(0).Build() },
            };
        }
    }

    
}
