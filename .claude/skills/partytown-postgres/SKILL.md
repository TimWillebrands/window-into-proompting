---
name: partytown-postgres
description: Use when making queries via mcp__postgres__query, inspecting PartyTown database state, counting or listing grains, or writing SQL against the Orleans database.
---

# PartyTown Postgres MCP Skill

## Database Overview

PostgreSQL + Microsoft Orleans. All domain state lives in a single grain storage table. The database also has the Apache AGE graph extension installed, currently unused.

**Key constraint:** grain state is stored as `payloadbinary bytea` — you cannot read actual state content via SQL. Queries are limited to grain identity and metadata.

## Schema Discovery

Before writing domain queries, discover what's there:

```sql
-- All tables (domain tables are in ag_catalog schema)
SELECT schemaname, tablename FROM pg_tables
WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
ORDER BY schemaname, tablename;

-- Columns for any table
SELECT column_name, data_type
FROM information_schema.columns
WHERE table_schema = 'ag_catalog' AND table_name = 'orleansstorage'
ORDER BY ordinal_position;

-- All grain types present
SELECT DISTINCT graintypestring FROM ag_catalog.orleansstorage;
```

## Core Domain Table: `ag_catalog.orleansstorage`

| Column | Type | Purpose |
|---|---|---|
| `graintypestring` | varchar | Grain type — primary filter |
| `grainidn0` | bigint | Numeric grain key (part 1) |
| `grainidn1` | bigint | Numeric grain key (part 2) |
| `grainidextensionstring` | varchar | String grain key (null for numeric-keyed grains) |
| `serviceid` | varchar | Service instance (`'default'`) |
| `payloadbinary` | bytea | Serialized state — not readable via SQL |
| `modifiedon` | timestamp | Last write time |
| `version` | integer | Optimistic concurrency version |

### Known Grain Types

| `graintypestring` | Role |
|---|---|
| `partyRoot` | Root grain tracking all Parties |
| `PartyTown.Grains.PartyGrain` | A single Party (owns ChatGroups and Personas) |
| `persona` | A single Persona (AI character) |
| `personaRoot` | Root grain tracking all Personas |

> **Terminology:** Party = top-level universe container. Persona = AI inhabitant. ChatGroup = conversation thread inside a Party.

### Useful Queries

```sql
-- Count by type
SELECT graintypestring, COUNT(*) FROM ag_catalog.orleansstorage GROUP BY graintypestring;

-- Most recently modified
SELECT graintypestring, grainidn0, modifiedon, version
FROM ag_catalog.orleansstorage ORDER BY modifiedon DESC LIMIT 20;

-- Specific grain type
SELECT grainidn0, grainidn1, modifiedon, version
FROM ag_catalog.orleansstorage WHERE graintypestring = 'PartyTown.Grains.PartyGrain';
```

## Limitations

- **Cannot read grain state** — `payloadbinary` is a binary-serialized .NET object. Read it through the running app, not SQL.
- **No cross-grain joins** — relationships are encoded inside binary state.
- **Grain identity** is either numeric (`grainidn0`/`grainidn1`) or string (`grainidextensionstring`), never both.

## Other Tables

- `orleansmembershiptable` — silo cluster membership (not useful for domain queries)
- `orleansmembershipversiontable` — cluster version counter
- `orleansquery` — SQL templates used by Orleans internally
