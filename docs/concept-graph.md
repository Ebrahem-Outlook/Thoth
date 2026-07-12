# Thoth Concept Graph

Thoth now has a contextual concept graph for dynamic memory. It is not a neural model and it is not proof of awareness; it is structured memory for concepts, aliases, relations, provenance, confidence, and context.

## Stored Objects

- `Concept`: typed entity such as a procedure or arithmetic operation.
- `ConceptAlias`: normalized lookup phrase for a concept.
- `ContextualRelation`: relation with participants, context keys, conditions, confidence, status, and source.
- `EvidenceSource`: provenance record for every stored concept or relation.

## Storage

The default runtime registers `SqliteConceptGraphStore` on the same local Thoth SQLite database. It creates:

- `concept_sources`
- `concepts`
- `concept_aliases`
- `contextual_relations`
- `relation_participants`
- `relation_context`

Indexes cover aliases, concept type/status, relation type/status, participants, and context key/value lookup.

## Activation

`ConceptActivationService` performs bounded local retrieval:

1. normalize input terms for matching only;
2. find exact/contained aliases;
3. expand one-hop contextual relations for activated concepts;
4. rank by confidence and context compatibility;
5. return a bounded concept/relation result.

It does not load the entire graph for every request.

## Context And Contradictions

Contradiction detection compares opposite relation types only when participants overlap and relation contexts are compatible. For example, `fire is safe` in controlled cooking context does not contradict `fire is dangerous` in an uncontrolled building context.

## Limitations

The current graph is a foundation: it stores and activates built-in deterministic concepts, but it is not yet used as the full production planning memory. Consolidation and learned relation promotion must remain conservative and provenance-preserving.
