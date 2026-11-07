-- The CDU's read-write role (ADR 0039). Idempotent; run as the database owner:
--   psql "$ADMIN_URL" -v cdu_password="$(openssl rand -hex 24)" -f tools/cdu/sql/cdu-role.sql
-- Grain state writes are futile (Orleans holds it in memory); memory graph writes land at once.
\set ON_ERROR_STOP on

SELECT 'CREATE ROLE cdu LOGIN' WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'cdu') \gexec
ALTER ROLE cdu WITH LOGIN PASSWORD :'cdu_password';

SELECT format('GRANT CONNECT, TEMPORARY ON DATABASE %I TO cdu', current_database()) \gexec

-- AGE without superuser: preloaded per session, catalog first on the path.
ALTER ROLE cdu SET session_preload_libraries = 'age';
ALTER ROLE cdu SET search_path = ag_catalog, "$user", public;

GRANT USAGE ON SCHEMA public, ag_catalog TO cdu;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO cdu;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO cdu;
GRANT SELECT ON ALL TABLES IN SCHEMA ag_catalog TO cdu;

-- The memory graph lives in the schema AGE made for it.
GRANT USAGE ON SCHEMA memory TO cdu;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA memory TO cdu;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA memory TO cdu;

-- Tables made later by the owner, such as new labels and migrations.
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO cdu;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO cdu;
ALTER DEFAULT PRIVILEGES IN SCHEMA memory GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO cdu;
ALTER DEFAULT PRIVILEGES IN SCHEMA memory GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO cdu;
