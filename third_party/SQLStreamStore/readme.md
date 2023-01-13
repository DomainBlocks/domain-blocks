# SQL Stream Store

These files are copied from https://github.com/SQLStreamStore/SQLStreamStore, due to the project no longer being maintained.

Only Postgres support has been imported at this stage.

The projects have been slightly modified for the purposes of DomainBlocks. Namely:

* Now targeting .NET 6
* Removal of LibLog, in favour of .NET logging
* Namespaces prefixed with `DomainBocks.ThirdParty`
* Upgrade Npgsql from 4.1.5 to 6.0.8

Licence: https://github.com/SQLStreamStore/SQLStreamStore/blob/master/LICENSE