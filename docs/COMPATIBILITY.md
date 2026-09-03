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
| **TypeScript SDK** (`sdk/typescript`) | **Not published.** Reference implementation only | Reference |
| **Python SDK** (`sdk/python`) | **Not published.** Reference implementation only | Reference |
| **Desk** (`desk/`) | Not published; built from source | Development |

The TypeScript and Python SDKs are reference implementations: they document how to talk to the
API from those ecosystems, carry version `0.0.0`, and are hard-gated against accidental publishing.
`pip install morphdb` installs an **unrelated project** — do not use it.

## Server ↔ .NET client

| Server | `MorphDB.Client` | Notes |
|--------|------------------|-------|
| 0.9.x – 0.11.x | 0.11.x | Verified compatible range — Docker-tested against the full live contract suite (2026-09-03). No wire-breaking change since 0.7.0 has narrowed this span; pin a version anyway, since a future minor may. |
| 0.7.x | 0.7.x | Project scoping via `X-Project-Id`. `X-Tenant-Id` is gone — 0.6.x clients cannot talk to a 0.7.x server. |
| 0.6.x | 0.6.x | Last version speaking `X-Tenant-Id`. |

Current downstream pair: `Formbase.* 0.9.0` ↔ MorphDB `0.11.x`. Formbase's own
[CHANGELOG](https://github.com/iyulab/formbase/blob/main/CHANGELOG.md) is the source of truth for
its full pairing history — this file states MorphDB's own compatibility contract, not a mirror of a
downstream project's release notes (an earlier drift here was exactly two documents holding the
same fact and disagreeing). Mixing across the `0.7.x`/`0.6.x` line fails at the first request.

## Reference SDK coverage

The reference SDKs track the core surface (schema, data CRUD, query, batch, bulk, aggregation).
They do **not** cover the Transactions and Views domains — consult `docs/API.md` for those.

## Container images

Use `0.7.1` or newer. Images `0.6.0` and `0.7.0` ship a broken HEALTHCHECK and report themselves
`unhealthy` forever; any orchestration waiting on that report waits forever.
