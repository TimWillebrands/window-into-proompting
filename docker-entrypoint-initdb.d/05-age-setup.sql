CREATE EXTENSION IF NOT EXISTS age;
LOAD 'age';

DO $$
BEGIN
  EXECUTE format('ALTER DATABASE %I SET search_path = ag_catalog, "$user", public', current_database());
END
$$;

