# Actor and world-avatar ontology projection

- **Date:** 2026-07-22
- **Status:** Implemented and verified in the Unity development harness.

## Decision and boundaries

Account character relations remain account-owned portable data. Unity projects
them only into the selected local avatar's runtime ontology view, then retracts
the projection when it is cleared. The projection never creates an authored
world Fact.

World-specific player meaning (role, team, class, progression) is a separate
world-avatar profile overlay. It is stored by `set_avatar_profile_relations`, a
revisioned World Authority command that is allowed only for the requesting
user's registered avatar.

Actor-scoped action candidate and action execution APIs are now reusable by
NPCs. The first `VillagerProfile` and social-village feature pack demonstrate
the data shape. The current NPC decision controller is intentionally local
development simulation; it must not be treated as server-authoritative shared
world execution until a server action catalog is published.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Project | `Self has_skill Talk` becomes a local avatar runtime Fact | `OntologyAccountProfileRelationProjectorTests` |
| Clear | The projected Fact is removed without deleting authored data | same test |
| NPC | Villager receives and executes a data-defined help action | `OntologyMultiActorActionTests` |
| Authority | World Authority Docker build succeeds | `scripts/verify-development.ps1` |
