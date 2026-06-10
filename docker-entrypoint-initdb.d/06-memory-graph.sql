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

-- Property-functional indexes. AGE's `->>` operator takes an *agtype* key on the right,
-- so the key must be written as an agtype string literal: '"id"', not 'id'. The bare-key
-- form ('id') raises `invalid input syntax for type agtype` and aborts initdb — this is
-- why earlier revisions of these indexes never materialised in the dev DB.

-- Stub vertex `id` uniqueness: MERGE (n:Persona {id: ...}) must dedup deterministically.
CREATE UNIQUE INDEX IF NOT EXISTS memory_persona_id_uq
  ON memory."Persona" ((properties ->> '"id"'));
-- Participant ids are unique only within a Party.
CREATE UNIQUE INDEX IF NOT EXISTS memory_participant_uq
  ON memory."Participant" ((properties ->> '"persona_id"'), (properties ->> '"party_id"'));

-- Concept dedup. `name` is the normalised (lowercase, trimmed) form;
-- `display` carries the human label as first written.
CREATE UNIQUE INDEX IF NOT EXISTS memory_concept_name_uq
  ON memory."Concept" ((properties ->> '"name"'));

-- Event lookup by Party powers the per-Party memory-graph viz (anchors on Event party_id
-- now that Room/Message vertices are gone).
CREATE INDEX IF NOT EXISTS memory_event_party_id_idx
  ON memory."Event" ((properties ->> '"party_id"'));

-- Event lookup by time (consolidation walk in later slices).
CREATE INDEX IF NOT EXISTS memory_event_created_at_idx
  ON memory."Event" ((properties ->> '"created_at"'));

-- Recollection-by-Participant lookup powers per-Persona memory retrieval.
CREATE INDEX IF NOT EXISTS memory_recollects_start_idx
  ON memory."RECOLLECTS" (start_id);
CREATE INDEX IF NOT EXISTS memory_recollects_end_idx
  ON memory."RECOLLECTS" (end_id);
