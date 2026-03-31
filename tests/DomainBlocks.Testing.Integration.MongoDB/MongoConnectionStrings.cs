namespace DomainBlocks.Testing.Integration.MongoDB;

public static class MongoConnectionStrings
{
    public const string Default = "mongodb://mongo1:27017,mongo2:27018,mongo3:27019/?replicaSet=rs0";

    public const string Atlas =
        "mongodb+srv://danielmsmith_db_user:NHQJHQ0OYMxOvAqm@domainblockstestcluster.cle2ydx.mongodb.net/?appName=DomainBlocksTestCluster";
}