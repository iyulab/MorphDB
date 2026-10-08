-- Moves an existing MorphDB database onto a service role PostgreSQL applies row-level security to.
--
-- Needed once, for a database that MorphDB used to reach as a superuser -- every database created
-- from the compose files before 0.19.0, where the image's bootstrap superuser (POSTGRES_USER=morph)
-- was also the service's login. MorphDB 0.19.0 refuses to start on a superuser or BYPASSRLS role.
--
-- Run it as a superuser, connected to the MorphDB database, with psql:
--
--   psql -v ON_ERROR_STOP=1 -v service_role=morphdb_service -v service_password='<password>' \
--        -d morphdb -f migrate-to-service-role.sql
--
-- then point ConnectionStrings__MorphDB at that role and start MorphDB. The bootstrap superuser
-- keeps its name and stays a superuser (PostgreSQL does not let it be demoted); it simply stops
-- being the service's login. If the role already exists it is reused, provided it is neither a
-- superuser nor BYPASSRLS; the password is then left as it is.
--
-- What moves is everything the old login owns where MorphDB keeps things: the database itself, the
-- global morphdb schema, every project schema recorded in morphdb._morph_projects, and every table,
-- view, materialized view, sequence and function in those schemas and in public -- where MorphDB
-- creates its data tables. An object in public that some other role owns is left alone. Ownership
-- is all that changes -- no data is touched. Safe to run again.

\set ON_ERROR_STOP on

-- psql variables are not interpolated inside a dollar-quoted block, so they travel as settings.
-- SET rather than SELECT set_config(): a SELECT would print the password back.
SET morphdb.migrate_service_role = :'service_role';
SET morphdb.migrate_service_password = :'service_password';

DO $$
DECLARE
    target text := current_setting('morphdb.migrate_service_role');
    -- The role MorphDB used to log in as: it created the global schema, so it owns it.
    previous oid := (SELECT nspowner FROM pg_namespace WHERE nspname = 'morphdb');
    schemas text[];
    schema_name text;
    object record;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = target) THEN
        EXECUTE format(
            'CREATE ROLE %I LOGIN PASSWORD %L NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE',
            target, current_setting('morphdb.migrate_service_password'));
    ELSIF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = target AND (rolsuper OR rolbypassrls)) THEN
        RAISE EXCEPTION 'role "%" is a superuser or has BYPASSRLS; choose another name, or run ALTER ROLE % NOSUPERUSER NOBYPASSRLS first', target, target;
    END IF;

    EXECUTE format('ALTER DATABASE %I OWNER TO %I', current_database(), target);

    -- Read the schema list up front: a loop driven by a query on morphdb._morph_projects would keep
    -- that table open, and its owner cannot change while it is.
    SELECT array_agg(name) INTO schemas FROM (
        SELECT 'morphdb' AS name
        UNION SELECT 'public'
        UNION SELECT system_schema FROM morphdb._morph_projects
        UNION SELECT data_schema FROM morphdb._morph_projects
    ) recorded;

    FOREACH schema_name IN ARRAY schemas
    LOOP
        CONTINUE WHEN NOT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = schema_name);

        -- public is the database's, not MorphDB's: on PostgreSQL 15+ it already follows the
        -- database owner, and on earlier versions anyone may create in it.
        IF schema_name <> 'public' THEN
            EXECUTE format('ALTER SCHEMA %I OWNER TO %I', schema_name, target);
        END IF;

        -- Tables, partitioned tables, views, materialized views, foreign tables, and sequences no
        -- column owns. A sequence a column owns (serial/identity) follows its table and cannot be
        -- moved on its own; indexes follow their table too.
        FOR object IN
            SELECT c.relname, c.relkind
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = schema_name
              AND c.relowner = previous
              AND c.relkind IN ('r', 'p', 'v', 'm', 'f', 'S')
              AND NOT (c.relkind = 'S' AND EXISTS (
                  SELECT 1 FROM pg_depend d
                  WHERE d.classid = 'pg_class'::regclass AND d.objid = c.oid AND d.deptype IN ('a', 'i')))
        LOOP
            EXECUTE format(
                'ALTER %s %I.%I OWNER TO %I',
                CASE object.relkind
                    WHEN 'v' THEN 'VIEW'
                    WHEN 'm' THEN 'MATERIALIZED VIEW'
                    WHEN 'f' THEN 'FOREIGN TABLE'
                    WHEN 'S' THEN 'SEQUENCE'
                    ELSE 'TABLE'
                END,
                schema_name, object.relname, target);
        END LOOP;

        FOR object IN
            SELECT p.oid::regprocedure AS signature
            FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = schema_name
              AND p.proowner = previous
        LOOP
            EXECUTE format('ALTER ROUTINE %s OWNER TO %I', object.signature, target);
        END LOOP;
    END LOOP;
END
$$;

-- Leave no password behind in the session settings.
RESET morphdb.migrate_service_password;
