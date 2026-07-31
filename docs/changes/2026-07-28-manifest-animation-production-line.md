# Manifest-driven animation production line

## Summary

- **Date:** 2026-07-28
- **Owner:** TOV development harness
- **Status:** Implemented and verified
- **Related request:** Make animation addition data-driven from triples/profile
  metadata plus uploaded clips, including base movement and combat animation.

## Intent

Replace repeated clip-specific runtime wiring with one validated production
line. A creator or developer supplies canonical metadata and a clip; generated
runtime projections then drive compatible actors without adding gameplay code.

## Data classification and boundaries

- Durable world data: Authority action definitions and equipment relations.
- Account profile data: the actor's animation repertoire.
- Ephemeral observation: movement, grounded state, vertical velocity, jump and
  landing phases.
- Unity presentation: clips, masks, layers, looping, transitions and root
  motion.
- Build/delivery metadata: provenance, license, checksum and delivery key.

`AnimationContentManifest` is the authoring source. `AnimationDatabase` and
profile animation-ID arrays are generated projections. Unity still communicates
only with World Authority for gameplay state.

## Ontology representation

Canonical English intent IDs connect Authority presentation metadata and Unity
content. No mesh, prefab, FBX, Animator state or GameObject name becomes a
gameplay exception. Actor repertoire is permission to present an animation;
gameplay capabilities remain independently Authority-owned.

## Enabled and removed behavior

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled | A compatible manifest entry resolves base, equipment or accepted action animation through an explicit actor repertoire. | `OntologyAnimationProductionLineTests` |
| Removed | Removing an entry removes its runtime definition and profile membership; stale generated data cannot recreate it. | Manifest synchronization implementation and removal regression |
| Invalid | Duplicate IDs, missing clips, unknown action intents, or mismatched published versions fail validation. | `OntologyAnimationProductionLineTests` |
| UGC staging | An FBX with attribution and license enters quarantine; only validated and approved content joins the manifest. | `OntologyAnimationUgcPipeline` |

## Performance and multiplayer impact

Per-frame state resolution is local and ephemeral. It produces no durable
Authority commands or database events. Only accepted versioned actions and
durable equipment changes cross the Authority boundary.

## Updated documents

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
