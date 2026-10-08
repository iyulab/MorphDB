-- MorphDB container bootstrap
--
-- This runs once, from docker-entrypoint-initdb.d, as the image's bootstrap superuser
-- (POSTGRES_USER), on an empty data directory only.
--
-- What it does is the one thing the service cannot do for itself: create the role the service
-- connects as. That role must be NOSUPERUSER and NOBYPASSRLS -- PostgreSQL applies no row-level
-- security to a superuser or a BYPASSRLS role, so MorphDB refuses to start on one -- and it owns the
-- database, which is what lets it create the global schema and one schema pair per project.
--
-- It deliberately defines no tables, and not even the morphdb schema. The schema has exactly one
-- source -- DdlBuilder.BuildGlobalSystemSchemaDdl(), which the service runs on every start
-- (PostgresSchemaLayerService.EnsureGlobalSchemaAsync). Creating any of it here would make the
-- bootstrap superuser its owner instead of the service's role.
--
-- No extensions are required: gen_random_uuid() is built into PostgreSQL 13+. Keeping this
-- extension-free is what lets morphdb run on a managed PostgreSQL where CREATE EXTENSION is gated
-- behind a server-parameter allow-list.
--
-- The password below is for local development only. In any other deployment create the role
-- yourself, with your own password, using the statements in the README's "Database role" section.

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'morph') THEN
        -- Reached when POSTGRES_USER is still 'morph' (the pre-0.19 compose files): the bootstrap
        -- superuser already holds the name, and it cannot be demoted.
        RAISE EXCEPTION 'role "morph" already exists; set POSTGRES_USER to another name (e.g. postgres) so "morph" can be created as the non-superuser service role';
    END IF;

    CREATE ROLE morph LOGIN PASSWORD 'morph' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;

    -- current_database(), not a literal name: the same script initialises every database this
    -- repository starts (the development bundle, the test bundle, the test suite's containers).
    EXECUTE format('ALTER DATABASE %I OWNER TO morph', current_database());
END
$$;
