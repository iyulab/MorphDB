-- MorphDB container bootstrap
--
-- This runs once, from docker-entrypoint-initdb.d, before the service starts.
--
-- It deliberately defines no tables. The schema has exactly one source --
-- DdlBuilder.BuildGlobalSystemSchemaDdl(), which the service runs on every start
-- (PostgresSchemaLayerService.EnsureGlobalSchemaAsync). A second copy here would drift from it,
-- and a drifted copy is worse than none: it hides the real bootstrap from anyone reading this file.
--
-- What only this script can do is grant privileges, because the service cannot grant them to its
-- own role. Everything else belongs to the builder.
--
-- No extensions are required: gen_random_uuid() is built into PostgreSQL 13+. Keeping this
-- extension-free is what lets morphdb run on a managed PostgreSQL where CREATE EXTENSION is gated
-- behind a server-parameter allow-list.

CREATE SCHEMA IF NOT EXISTS morphdb;

-- Container role privileges. ALTER DEFAULT PRIVILEGES covers the tables the service creates after
-- this script has run -- a plain GRANT ON ALL TABLES would only reach tables that already exist.
GRANT USAGE, CREATE ON SCHEMA morphdb TO morph;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA morphdb TO morph;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA morphdb TO morph;
ALTER DEFAULT PRIVILEGES IN SCHEMA morphdb GRANT ALL PRIVILEGES ON TABLES TO morph;
ALTER DEFAULT PRIVILEGES IN SCHEMA morphdb GRANT ALL PRIVILEGES ON SEQUENCES TO morph;
