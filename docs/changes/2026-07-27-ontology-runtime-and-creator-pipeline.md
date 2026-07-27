# Runtime Fact Ownership and Creator Pipeline

## Summary

- **Date:** 2026-07-27
- **Owner:** TOV development
- **Status:** Implemented and targeted verification passed; full Unity suite
  pending an unlocked Editor project
- **Request:** Remove hard-coded ontology/runtime ownership problems before
  implementing specialist NPC functions.

## Intent

Make the player/world runtime safe enough for the first prompt-driven UGC
vertical slice without turning Unity names, observations, or temporary creator
UI into durable gameplay truth.

## Data classification

- [ ] Account profile
- [x] Durable authored world data
- [x] Runtime observation
- [x] Inferred state
- [x] Unity presentation
- [x] Transport / permission / infrastructure

## Decisions and boundaries

- Deleted the duplicate scene sample with `entityId: Player`; the real player
  remains profile-driven. The sample's FireSword and skill Facts were not
  migrated because they were neither verified account profile data nor authored
  world state.
- Runtime standing tile, proximity, click intent, and simulation tick use
  `RuntimeObservation` contributions and retract only that origin.
- Runtime tile changes no longer create saveable session events.
- `OntologyObject.EntityId` has no GameObject-name fallback. New placed entities
  use their Authority identity GUID; save version 8 separates identity and
  display name.
- Creator roles and temporary appearances come from
  `CreatorServiceCatalog.asset`.
- NPC action priority and local auto-run require an explicit
  `OntologyNpcDecisionPolicy`; the controller has no hidden help/talk default.
- A World Architect prompt creates an in-memory Draft and route only. It writes
  no world Fact and sends no Authority command.

## Ontology expression

No new gameplay predicate was added. This change protects contribution origins
and stable subjects. Canonical specialist identifiers remain:
`WorldArchitect`, `ResourceMaker`, `OntologySteward`, `PhysicsEngineer`,
`RuleEngineer`, `QuestDesigner`, `UiDesigner`, and `QaPublisher`.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Runtime observation removed | Only `RuntimeObservation` contribution is removed | `OntologyWorldStateTests` |
| Durable equal Fact added later | Runtime retraction preserves it | `OntologyWorldStateTests` |
| World rebuild | Owned runtime single value is reasserted | `OntologyWorldStateTests` |
| GameObject renamed | Stable entity ID does not change | `OntologyStableEntityIdentityTests` |
| Valid architect prompt | Ordered Draft route; zero world Facts | `OntologyCreatorWorkDraftTests` |
| Short prompt / missing architect | Draft rejected | `OntologyCreatorWorkDraftTests` |
| Duplicate Player | One explicit `entityId: Player` remains in `TormiaWorld` | Scene audit |

Verification completed on 2026-07-27:

- `scripts/verify-development.ps1` passed.
- `scripts/verify-development.ps1 -RequireServices` passed with PostgreSQL,
  Redis, and World Authority healthy.
- Core, Unity, UI, Editor, Core.Tests, and Unity.Tests assemblies compiled with
  no errors.
- Five targeted runtime-observation and creator-draft tests passed.
- The full EditMode runner could not open the project while the interactive
  Unity Editor owned the project lock. This is an environment constraint, not
  a test failure; run the full suite after closing or reconnecting the Editor.

## Performance and multiplayer impact

The change reduces durable history pressure by removing movement observation
events. Creator drafts remain local and ephemeral. No new database or network
write path was added. Durable creator results must use revisioned,
idempotent World Authority commands in later slices.

## Updated documents

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] English/Korean localization CSV
- [x] Tests and change record
