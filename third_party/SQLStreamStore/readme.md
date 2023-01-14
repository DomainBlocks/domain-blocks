# SQL Stream Store

These files are copied from https://github.com/SQLStreamStore/SQLStreamStore (commit
[c55d9db](https://github.com/SQLStreamStore/SQLStreamStore/commit/c55d9db469ea00ee3cb44dd2efac46041061eef9)), due to the
project no longer being maintained.

At this stage, only Postgres support has been included.

The projects have been slightly modified for the purposes of DomainBlocks. Namely:

* Now targeting .NET 6
* Removal of LibLog, in favour of .NET logging (via `DomainBlocks.Logging`)
* Namespaces changed to `DomainBlocks.ThirdParty.SqlStreamStore.*`
* Upgrade Npgsql from 4.1.5 to 6.0.8

Licence: https://github.com/SQLStreamStore/SQLStreamStore/blob/master/LICENSE