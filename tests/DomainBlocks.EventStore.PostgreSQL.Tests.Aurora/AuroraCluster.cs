using System.Net;
using System.Text.Json;
using Amazon.RDS;
using Amazon.RDS.Model;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Npgsql;
using Filter = Amazon.RDS.Model.Filter;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Aurora;

/// <summary>
/// The Aurora cluster that <c>DBX_AURORA_CLUSTER</c> names, reached with the AWS SDK's default credentials and region.
/// </summary>
internal sealed class AuroraCluster(string clusterId) : IDisposable
{
    private const string ClusterVariable = "DBX_AURORA_CLUSTER";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly AmazonRDSClient _rds = new();

    public static AuroraCluster FromEnvironment()
    {
        var clusterId = Environment.GetEnvironmentVariable(ClusterVariable);

        if (string.IsNullOrEmpty(clusterId))
            throw new InvalidOperationException($"Set {ClusterVariable} to the cluster's identifier. See README.md.");

        return new AuroraCluster(clusterId);
    }

    /// <summary>
    /// Returns a connection string for the master user through the cluster endpoint, which reaches the writer.
    /// </summary>
    public async Task<string> GetMasterConnectionStringAsync(CancellationToken cancellationToken)
    {
        var cluster = await DescribeClusterAsync(cancellationToken);

        using var secrets = new AmazonSecretsManagerClient();

        var secret = await secrets.GetSecretValueAsync(
            new GetSecretValueRequest { SecretId = cluster.MasterUserSecret.SecretArn },
            cancellationToken);

        using var credentials = JsonDocument.Parse(secret.SecretString);

        return new NpgsqlConnectionStringBuilder
        {
            Host = cluster.Endpoint,
            Port = cluster.Port ?? 5432,
            Username = credentials.RootElement.GetProperty("username").GetString(),
            Password = credentials.RootElement.GetProperty("password").GetString(),
            Database = "postgres",
            SslMode = SslMode.Require
        }.ConnectionString;
    }

    /// <summary>
    /// Returns the endpoints of the writer instance and of a reader instance.
    /// </summary>
    public async Task<(DnsEndPoint Writer, DnsEndPoint Reader)> GetInstanceEndpointsAsync(
        CancellationToken cancellationToken)
    {
        var (writerId, readerId) = await GetRolesAsync(cancellationToken);
        var instances = await DescribeInstancesAsync(cancellationToken);

        return (EndpointOf(writerId), EndpointOf(readerId));

        DnsEndPoint EndpointOf(string instanceId)
        {
            var endpoint = instances.Single(x => x.DBInstanceIdentifier == instanceId).Endpoint;
            return new DnsEndPoint(endpoint.Address, endpoint.Port ?? 5432);
        }
    }

    /// <summary>
    /// Fails the cluster over to a reader and waits until the reader is the writer and every instance is available.
    /// </summary>
    public async Task FailOverAsync(CancellationToken cancellationToken)
    {
        var (_, readerId) = await GetRolesAsync(cancellationToken);

        await _rds.FailoverDBClusterAsync(
            new FailoverDBClusterRequest { DBClusterIdentifier = clusterId, TargetDBInstanceIdentifier = readerId },
            cancellationToken);

        while (true)
        {
            await Task.Delay(PollInterval, cancellationToken);

            var cluster = await DescribeClusterAsync(cancellationToken);
            var instances = await DescribeInstancesAsync(cancellationToken);

            if (cluster.Status == "available" &&
                cluster.DBClusterMembers.Any(x => x.DBInstanceIdentifier == readerId && x.IsClusterWriter == true) &&
                instances.All(x => x.DBInstanceStatus == "available"))
            {
                return;
            }
        }
    }

    public void Dispose() => _rds.Dispose();

    private async Task<(string WriterId, string ReaderId)> GetRolesAsync(CancellationToken cancellationToken)
    {
        var members = (await DescribeClusterAsync(cancellationToken)).DBClusterMembers;

        return (
            members.Single(x => x.IsClusterWriter == true).DBInstanceIdentifier,
            members.First(x => x.IsClusterWriter != true).DBInstanceIdentifier);
    }

    private async Task<DBCluster> DescribeClusterAsync(CancellationToken cancellationToken)
    {
        var response = await _rds.DescribeDBClustersAsync(
            new DescribeDBClustersRequest { DBClusterIdentifier = clusterId },
            cancellationToken);

        return response.DBClusters.Single();
    }

    private async Task<List<DBInstance>> DescribeInstancesAsync(CancellationToken cancellationToken)
    {
        var response = await _rds.DescribeDBInstancesAsync(
            new DescribeDBInstancesRequest { Filters = [new Filter { Name = "db-cluster-id", Values = [clusterId] }] },
            cancellationToken);

        return response.DBInstances;
    }
}