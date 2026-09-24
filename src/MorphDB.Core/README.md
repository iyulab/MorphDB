# MorphDB.Core

The contracts, models and exceptions of the MorphDB engine, for **embedding MorphDB in your own
process**: a .NET host running schema and data operations in-process against its own PostgreSQL, with
no server in front. [`MorphDB.Npgsql`](https://www.nuget.org/packages/MorphDB.Npgsql) implements them.

This is the advanced path. If you are talking to a running MorphDB server, the only package you need
is [`MorphDB.Client`](https://www.nuget.org/packages/MorphDB.Client).

PostgreSQL is the only engine MorphDB runs on; `MorphDB.Core` is split from `MorphDB.Npgsql` to keep
the contracts apart from their implementation, not as an interface for plugging in another database.
All three packages move together on one version.

Source, documentation and license: <https://github.com/iyulab/morphdb>
