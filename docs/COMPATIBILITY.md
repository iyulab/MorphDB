# MorphDB Version Compatibility Matrix

This document defines the compatibility relationships between MorphDB components: Server, SDKs, and Desk client.

## Component Versions

| Component | Current Version | Status |
|-----------|----------------|--------|
| **Server** (MorphDB.Service) | 0.10.0 | Production Ready |
| **Python SDK** (morphdb) | 0.13.0 | Beta |
| **TypeScript SDK** (@morphdb/client) | 0.13.0 | Beta |
| **Desk** (morphdb-studio) | 0.1.0 | Alpha |

## Compatibility Matrix

### Server ↔ SDK Compatibility

| Server Version | Python SDK | TypeScript SDK | Notes |
|---------------|------------|----------------|-------|
| 0.10.x | 0.13.x | 0.13.x | Current stable combination |
| 0.9.x | 0.12.x | 0.12.x | Previous release |
| 0.8.x | 0.11.x | 0.11.x | Legacy support |

### Server ↔ Desk Compatibility

| Server Version | Desk Version | Notes |
|---------------|--------------|-------|
| 0.10.x | 0.1.x | Current development |
| 0.9.x | 0.1.x | Compatible with warnings |

## API Version Support

The MorphDB Server maintains backward compatibility within minor versions:

| API Version | Introduced | Deprecated | Removed | Features |
|-------------|-----------|------------|---------|----------|
| v1 | 0.1.0 | - | - | Core CRUD, Schema, Query |

## Feature Compatibility

### Core Features

| Feature | Server | Python SDK | TS SDK | Desk |
|---------|--------|-----------|--------|------|
| Schema CRUD | ✅ | ✅ | ✅ | ✅ |
| Data CRUD | ✅ | ✅ | ✅ | ✅ |
| Batch Operations | ✅ | ✅ | ✅ | 🔄 |
| Query Filters | ✅ | ✅ | ✅ | ✅ |
| Pagination | ✅ | ✅ | ✅ | ✅ |
| Real-time (SignalR) | ✅ | 🔄 | 🔄 | 🔄 |
| GraphQL | ✅ | ⏳ | ⏳ | ⏳ |
| OData | ✅ | ⏳ | ⏳ | ⏳ |
| Webhooks | ✅ | ⏳ | ⏳ | ⏳ |

**Legend**: ✅ Supported | 🔄 In Progress | ⏳ Planned | ❌ Not Supported

### Filter Operators

| Operator | Server | Python SDK | TS SDK | Description |
|----------|--------|-----------|--------|-------------|
| EQ | ✅ | ✅ | ✅ | Equals |
| NEQ | ✅ | ✅ | ✅ | Not equals |
| GT | ✅ | ✅ | ✅ | Greater than |
| GTE | ✅ | ✅ | ✅ | Greater than or equal |
| LT | ✅ | ✅ | ✅ | Less than |
| LTE | ✅ | ✅ | ✅ | Less than or equal |
| CONTAINS | ✅ | ✅ | ✅ | String contains |
| STARTSWITH | ✅ | ✅ | ✅ | String starts with |
| ENDSWITH | ✅ | ✅ | ✅ | String ends with |
| IN | ✅ | ✅ | ✅ | Value in array |
| ISNULL | ✅ | ✅ | ✅ | Is null check |

### Column Types

| Type | Server | Python SDK | TS SDK | PostgreSQL |
|------|--------|-----------|--------|------------|
| text | ✅ | ✅ | ✅ | VARCHAR/TEXT |
| integer | ✅ | ✅ | ✅ | INTEGER |
| bigint | ✅ | ✅ | ✅ | BIGINT |
| decimal | ✅ | ✅ | ✅ | DECIMAL |
| boolean | ✅ | ✅ | ✅ | BOOLEAN |
| date | ✅ | ✅ | ✅ | DATE |
| timestamp | ✅ | ✅ | ✅ | TIMESTAMP |
| uuid | ✅ | ✅ | ✅ | UUID |
| jsonb | ✅ | ✅ | ✅ | JSONB |

## Breaking Changes

### v0.10.0 → v0.11.0 (Planned)

- No breaking changes expected

### v0.9.0 → v0.10.0

- Added system columns (`_created_at`, `_updated_at`, `_version`)
- Schema response format expanded

## Upgrade Guidelines

### Server Upgrade

1. **Backup database** before upgrading
2. **Check SDK compatibility** with target server version
3. **Run migrations** if required
4. **Update SDKs** to compatible versions

### SDK Upgrade

1. **Check server compatibility** before upgrading SDK
2. **Review changelog** for breaking changes
3. **Update type definitions** if using TypeScript
4. **Run integration tests** after upgrade

### Desk Upgrade

1. **Close all connections** before upgrading
2. **Export connection settings** if needed
3. **Verify server compatibility**

## Testing Compatibility

### Integration Test Commands

```bash
# Start test server
docker compose -f docker-compose.test.yml up -d

# Run SDK integration tests
cd sdk/python && pytest -m integration
cd sdk/typescript && npm run test:integration

# Run Desk integration tests
cd desk && npm run test:integration
```

### Compatibility Test Matrix

The CI pipeline runs compatibility tests against:

| Test Scenario | Components | Status |
|--------------|------------|--------|
| Server + Python SDK | Latest | ✅ |
| Server + TypeScript SDK | Latest | ✅ |
| Server + Desk | Latest | ✅ |
| Server (prev) + SDK (curr) | N-1 → N | ✅ |
| Server (curr) + SDK (prev) | N → N-1 | ✅ |

## Support Policy

| Version Type | Support Period | Compatibility |
|--------------|----------------|---------------|
| Major (X.0.0) | 12 months | Breaking changes allowed |
| Minor (0.X.0) | 6 months | Backward compatible |
| Patch (0.0.X) | 3 months | Bug fixes only |

## Known Issues

### Current Compatibility Issues

| Issue | Affected Versions | Workaround | Status |
|-------|------------------|------------|--------|
| None | - | - | - |

### Resolved Issues

| Issue | Affected Versions | Fixed In | Resolution |
|-------|------------------|----------|------------|
| - | - | - | - |

## Version History

### Server (MorphDB.Service)

- **0.10.0** - Production hardening, system columns
- **0.9.5** - Philosophy alignment
- **0.9.0** - GraphQL, OData support

### Python SDK (morphdb)

- **0.13.0** - Integration test support
- **0.12.0** - Batch operations

### TypeScript SDK (@morphdb/client)

- **0.13.0** - Integration test support
- **0.12.0** - Real-time client

### Desk (morphdb-studio)

- **0.1.0** - Initial alpha release

---

*Last Updated: 2025-01-05*
*Document Version: 1.0.0*
