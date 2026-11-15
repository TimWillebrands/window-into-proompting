LOAD 'age';
SET search_path = ag_catalog, "$user", public;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM ag_catalog.ag_graph WHERE name = 'memory') THEN
    PERFORM ag_catalog.create_graph('memory');
  END IF;
END
$$;

DO $$
DECLARE
  lbl text;
BEGIN
  FOREACH lbl IN ARRAY ARRAY['Persona','Participant','Concept','Event']
  LOOP
    IF NOT EXISTS (
      SELECT 1 FROM ag_catalog.ag_label
       WHERE name = lbl AND graph = (SELECT graphid FROM ag_catalog.ag_graph WHERE name = 'memory')
    ) THEN
      PERFORM ag_catalog.create_vlabel('memory'::cstring, lbl::cstring);
    END IF;
  END LOOP;
END
$$;

DO $$
DECLARE
  lbl text;
BEGIN
  FOREACH lbl IN ARRAY ARRAY['HAS_PARTICIPANT','RECOLLECTS','ABOUT','STANCE']
  LOOP
    IF NOT EXISTS (
      SELECT 1 FROM ag_catalog.ag_label
       WHERE name = lbl AND graph = (SELECT graphid FROM ag_catalog.ag_graph WHERE name = 'memory')
    ) THEN
      PERFORM ag_catalog.create_elabel('memory'::cstring, lbl::cstring);
    END IF;
  END LOOP;
END
$$;

