-- Memory graph (Recollection capture).
-- Vertices and edges per ADR 0006/0007 + the shape reshape in ADR 0014. Single graph
-- `memory`; tenant scoping rides `party_id` *properties* on Participant and Event — there
-- is no Party stub vertex. Stubs (Persona/Participant) carry only ids mirroring the
-- grain-side source of truth; Event carries its anchor (room_id/party_id/anchor_message_id)
-- as properties rather than ANCHORED_TO edges to Message vertices.

LOAD 'age';
SET search_path = ag_catalog, "$user", public;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM ag_catalog.ag_graph WHERE name = 'memory') THEN
    PERFORM ag_catalog.create_graph('memory');
  END IF;
END
$$;

-- Vertex labels.
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

-- Edge labels. STANCE is included up front so later slices don't need a DDL change.
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

-- Indexes live in EF migrations (ADR 0008), so every DB gets them: backend/Data/Migrations.
