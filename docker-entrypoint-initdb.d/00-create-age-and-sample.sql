-- create extension and load age so cypher works
CREATE EXTENSION IF NOT EXISTS age;
LOAD 'age';

-- make ag_catalog first in search path for convenience
DO $$
BEGIN
  EXECUTE format('ALTER DATABASE %I SET search_path = ag_catalog, "$user", public', current_database());
END
$$;

-- an example cypher query you can run manually:
-- SELECT * FROM cypher('dev_graph', $$ CREATE (n:Person {name: 'Alice'}) RETURN n $$) AS (n agtype);
