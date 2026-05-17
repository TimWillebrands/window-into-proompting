-- Memory graph (slice 1: Recollection capture).
-- Vertices and edges per ADR 0006/0007. Single graph `memory`; tenant scoping via
-- a Party stub vertex linked from each Participant. Stubs (Persona/Participant/Party/
-- Room/Message) carry only an `id` mirroring the grain-side source of truth.

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
  FOREACH lbl IN ARRAY ARRAY['Persona','Participant','Party','Room','Message','Concept','Event']
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
  FOREACH lbl IN ARRAY ARRAY['HAS_PARTICIPANT','IN_PARTY','RECOLLECTS','ANCHORED_TO','ABOUT','STANCE']
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

-- Stub vertex `id` uniqueness: MERGE (n:Persona {id: ...}) must dedup deterministically.
CREATE UNIQUE INDEX IF NOT EXISTS memory_persona_id_uq
  ON memory."Persona" ((properties ->> 'id'));
CREATE UNIQUE INDEX IF NOT EXISTS memory_party_id_uq
  ON memory."Party" ((properties ->> 'id'));
CREATE UNIQUE INDEX IF NOT EXISTS memory_room_id_uq
  ON memory."Room" ((properties ->> 'id'));
-- Message ids (int) are unique only within a Room; participant ids only within a Party.
CREATE UNIQUE INDEX IF NOT EXISTS memory_message_uq
  ON memory."Message" ((properties ->> 'room_id'), (properties ->> 'id'));
CREATE UNIQUE INDEX IF NOT EXISTS memory_participant_uq
  ON memory."Participant" ((properties ->> 'persona_id'), (properties ->> 'party_id'));

-- Concept dedup. `name` is the normalised (lowercase, trimmed) form;
-- `display` carries the human label as first written.
CREATE UNIQUE INDEX IF NOT EXISTS memory_concept_name_uq
  ON memory."Concept" ((properties ->> 'name'));

-- Event lookup by time (consolidation walk in later slices).
CREATE INDEX IF NOT EXISTS memory_event_created_at_idx
  ON memory."Event" (((properties ->> 'created_at')));

-- Recollection-by-Participant lookup powers per-Persona memory retrieval.
CREATE INDEX IF NOT EXISTS memory_recollects_start_idx
  ON memory."RECOLLECTS" (start_id);
CREATE INDEX IF NOT EXISTS memory_recollects_end_idx
  ON memory."RECOLLECTS" (end_id);
