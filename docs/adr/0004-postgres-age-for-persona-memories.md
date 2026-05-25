# Postgres + Apache AGE for Persona memories

Persona memories will be stored as a **graph** in Postgres via the **Apache AGE** extension — the same database that already backs Orleans clustering and grain persistence. The graph is per-Party-per-Persona scope (a Persona has different memories in different Parties). Memories will store both event/relationship structure and embeddings for similarity lookup.

The memory feature itself is **not yet built**. This ADR records the bet on AGE, not a completed implementation.

## Why a graph

A Persona's memories aren't flat rows — they're a mesh: "I think Vlad is impatient" links to "the time Vlad cut me off" links to the original Message and to the Room it happened in. Querying for "what does X think of Y" naturally walks this mesh.

A graph DB lets us:

- Store memories as nodes with typed edges (`thinks-about`, `triggered-by`, `said-in-room`).
- Run cheap graph traversals when a Persona is generating ("walk from {this-Persona} via `thinks-about` toward {topic}, limit 3 hops").
- Mix structural lookup (graph walk) with semantic lookup (embedding-nearest-neighbor on memory nodes) without a second store.

## Why AGE and not Neo4j / a dedicated graph DB

- Postgres is **already in the stack** for Orleans clustering and grain persistence. Adding AGE is a `CREATE EXTENSION`, not a new piece of infra to operate.
- Memories will be queried in the same transactions as other state — staying in Postgres keeps that simple.
- AGE speaks openCypher, which is the standard graph query language — not learning a one-DB-specific dialect.
- If AGE turns out to be wrong (immature, slow, abandoned), we lose the *graph* shape, not the *database* — the memories can be migrated to a relational shape within the same Postgres without a cross-database move.

## Consequences

- AGE is less mature than Neo4j. Expect rough edges, especially in Orleans + Entity Framework toolchains.
- The Postgres container image is a custom build (AGE-enabled). Anyone running the stack outside Aspire needs to know that.
- This is a **bet, not a commitment**. If the memory feature ships and AGE bites, the structural escape hatch is relational tables in the same DB.
