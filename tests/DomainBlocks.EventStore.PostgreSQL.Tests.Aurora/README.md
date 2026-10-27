# Aurora tests

These tests run live subscriptions on Aurora PostgreSQL, which the local test servers can't stand in for. Aurora makes
a commit durable in a cluster volume that every instance shares, so it has no synchronous standbys, and a writer
failover moves the writer to another instance. The tests check that:

- a live subscription works: the feed creates its failover slot and passes the store's server checks;
- a subscription whose endpoint still reaches the old writer, now a reader, keeps trying, and delivers events once
  the endpoint reaches the writer;
- across a writer failover, a subscription delivers every event in the log once, in order;
- a plain slot, which PostgreSQL wouldn't copy to a standby, survives a writer failover with its changes, so the feed
  resumes there because of Aurora's shared storage rather than PostgreSQL's slot synchronization.

They are explicit tests, so a plain `dotnet test` skips them.

## Running them

`run.sh` creates the cluster with CloudFormation, runs the tests, and deletes the cluster:

```bash
tests/DomainBlocks.EventStore.PostgreSQL.Tests.Aurora/run.sh           # create, test, delete
tests/DomainBlocks.EventStore.PostgreSQL.Tests.Aurora/run.sh --keep    # keep the cluster for another run
tests/DomainBlocks.EventStore.PostgreSQL.Tests.Aurora/run.sh --delete  # delete a kept cluster
```

It needs bash (Git Bash on Windows), the .NET SDK, and the AWS CLI v2 with credentials that can manage CloudFormation
stacks, RDS clusters, EC2 security groups and Secrets Manager secrets. `AWS_REGION` picks the region, `eu-west-3` by
default, and `DBX_AURORA_CLUSTER` the stack and cluster name, `dbx-aurora-test` by default.

To run the tests from an IDE, keep a cluster with `run.sh --keep`, then set `AWS_REGION` and `DBX_AURORA_CLUSTER` for
the test run. The failover test swaps the writer and the reader, and the tests look up which is which, so a kept
cluster can be reused.

## The cluster

`aurora-test-cluster.yaml` creates:

- an Aurora PostgreSQL 17 cluster with a Serverless v2 writer and reader, at 0.5 to 2 capacity units each, without
  deletion protection or a final snapshot;
- a cluster parameter group with `rds.logical_replication = 1`;
- a security group in the default VPC that admits port 5432 from this machine's public IP only.

The master password is kept in Secrets Manager and deleted with the cluster. The tests create an ordinary role with
`rds_replication` and run the store as it.

A run takes about half an hour, most of it creating and deleting the cluster, and costs well under a dollar. With
logical replication on, Aurora never pauses the instances, so a kept cluster costs at least one capacity unit-hour
every hour, $0.14 in `eu-west-3` in October 2026, until it is deleted.
