# MorphDB Version Compatibility

One number covers everything: a release publishes the git tag `vX.Y.Z`, the container image
`X.Y.Z`, and the NuGet packages `X.Y.Z` together. A client and a server that share a version are a
compatible pair. This is a 0.x line — a minor release may break you, so pin a version rather than
`latest`; the newest one is the newest [tag](https://github.com/iyulab/MorphDB/tags).

## Components

| Component | Distribution | Status |
|-----------|--------------|--------|
| **Server** (`ghcr.io/iyulab/morphdb`) | Container image, versioned with the repo | Released |
| **.NET client** (`MorphDB.Client`) | NuGet, versioned with the repo | Released |
| **TypeScript SDK** (`sdk/typescript`) | **Not published.** Archived | Archived |
| **Python SDK** (`sdk/python`) | **Not published.** Archived | Archived |
| **Desk** (`desk/`) | Not published; built from source | Development |

The TypeScript and Python SDKs are **archived**: the source stays in the repository as a record of
how the API was called from those ecosystems, frozen at the `0.11.x` contract, but it is not
maintained, not tested against the server, and not kept in step with later contract changes —
treat it as a starting point to read, not a client to run. Both carry version `0.0.0` and are
hard-gated against accidental publishing. `pip install morphdb` installs an **unrelated project** —
do not use it. The supported clients are the REST/GraphQL API itself and `MorphDB.Client` (.NET).

## Server ↔ .NET client

| Server | `MorphDB.Client` | Notes |
|--------|------------------|-------|
| 0.12.x | 0.12.x | Wire changes on the real-time and export surfaces: an export request that names `filter` or `orderBy` is refused (`400`), the `Subscribe` hub method takes one argument, and a webhook delivery is camelCase (`recordId`). A `0.11.x` client works against a `0.12.x` server for every REST and GraphQL call except an export that sets `filter`/`orderBy` — those members were never applied and are now rejected; its `SubscribeAsync`, which sent two arguments and never received a change, is now refused by the hub's binder instead of silently succeeding — real-time needs the `0.12.x` client. Verified: Formbase 0.10.0 (`MorphDB.Client` 0.11.1) runs its live MorphDB suite unchanged against the `0.12.0` image (310/310, 2026-09-13). |
| 0.9.x – 0.11.x | 0.11.x | Verified compatible range — Docker-tested against the full live contract suite (2026-09-03). No wire-breaking change since 0.7.0 has narrowed this span; pin a version anyway, since a future minor may. |
| 0.7.x | 0.7.x | Project scoping via `X-Project-Id`. `X-Tenant-Id` is gone — 0.6.x clients cannot talk to a 0.7.x server. |
| 0.6.x | 0.6.x | Last version speaking `X-Tenant-Id`. |

Current downstream pair: `Formbase.* 0.10.1` ↔ MorphDB `0.12.x`. Formbase's own
[CHANGELOG](https://github.com/iyulab/formbase/blob/main/CHANGELOG.md) is the source of truth for
its full pairing history — this file states MorphDB's own compatibility contract, not a mirror of a
downstream project's release notes (an earlier drift here was exactly two documents holding the
same fact and disagreeing). Mixing across the `0.7.x`/`0.6.x` line fails at the first request.

## Container images

Use `0.7.1` or newer. Images `0.6.0` and `0.7.0` ship a broken HEALTHCHECK and report themselves
`unhealthy` forever; any orchestration waiting on that report waits forever.
