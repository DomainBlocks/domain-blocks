using System;
using DomainBlocks.Projections.EventStore;
using DomainBlocks.Projections.Sql.Tests.Fakes;
using EventStore.Client;

namespace DomainBlocks.Projections.Sql.Tests
{
    public class SqlProjectionScenario
    {
        public FakeJsonEventPublisher Publisher { get; } = new();
        public EventDispatcher<EventRecord, object> Dispatcher { get; private set; }
        public FakeDbConnector DbConnector { get; private set; }
        public static EventDispatcherConfiguration DefaultDispatcherConfig { get; } = EventDispatcherConfiguration.ReadModelDefaults with
            {
            ProjectionHandlerTimeout =
            TimeSpan.FromHours(2)
            };

        public SqlContextSettings SqlContextSettings { get; }

        private EventDispatcherConfiguration DispatcherConfig { get; }

        public SqlProjectionScenario(EventDispatcherConfiguration dispatcherConfig = null, SqlContextSettings sqlContextSettings = null)
        {
            DispatcherConfig = dispatcherConfig ?? DefaultDispatcherConfig;
            SqlContextSettings = sqlContextSettings ?? SqlContextSettings.Default;
            CreateDispatcher();
        }

        public void CreateDispatcher()
        {
            var registryBuilder = new ProjectionRegistryBuilder();
            var projection = new FakeSqlProjection(SqlContextSettings);

            DbConnector = (FakeDbConnector)projection.DbConnector;

            registryBuilder.Event<TestEvent>()
                           .FromName(TestEvent.Name)
                           .ToSqlProjection(projection)
                           .ParameterMappings(("Col1", e => e.Id),
                                              ("Col2", e => e.Value))
                           .ExecutesUpsert();

            registryBuilder.Event<MultipleNamesEvent>()
                           .FromNames(MultipleNamesEvent.Name, MultipleNamesEvent.OtherName)
                           .ToSqlProjection(projection)
                           .ParameterMappings(("Col1", e => e.Id),
                                              ("Col3", e => e.Data))
                           .ExecutesUpsert();

            registryBuilder.Event<UpsertCustomSqlEvent>()
                           .FromName(UpsertCustomSqlEvent.Name)
                           .ToSqlProjection(projection)
                           .ParameterMappings(("Col1", e => e.Id),
                                              ("Col2", e => e.Value))
                           .ExecutesUpsert()
                           .ExecutesCustomSql(UpsertCustomSqlEvent.CustomSqlText);

            registryBuilder.Event<DeleteCustomSqlEvent>()
                           .FromName(DeleteCustomSqlEvent.Name)
                           .ToSqlProjection(projection)
                           .ParameterMappings(("Col1", e => e.Id),
                                              ("Col2", e => e.Value))
                           .ExecutesDelete()
                           .ExecutesCustomSql(DeleteCustomSqlEvent.CustomSqlText);

            var registry = registryBuilder.Build();
            var dispatcherConfig = EventDispatcherConfiguration.ReadModelDefaults with { ProjectionHandlerTimeout =
                                       TimeSpan.FromHours(2)};

            var dispatcher = new EventDispatcher<EventRecord, object>(Publisher,
                                                                      registry.EventProjectionMap,
                                                                      registry.ProjectionContextMap,
                                                                      new EventRecordJsonDeserializer(),
                                                                      registry.EventNameMap,
                                                                      dispatcherConfig);

            Dispatcher = dispatcher;
        }
    }
}