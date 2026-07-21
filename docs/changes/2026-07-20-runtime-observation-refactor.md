# Runtime Observation Ownership Refactor

## Summary

- **Date:** 2026-07-20
- **Owner:** Tormia development
- **Status:** Implemented and verified
- **Related request:** Hard-coding, duplicate-code, and architecture refactor

## Intent

Keep world observations reusable and safe while new environment, equipment, and
physics content is added. A runtime sensor must not remove an authored fact or
a fact published by another adapter merely because its own observation changes.

## Data classification

- [x] Runtime observation
- [x] Unity presentation

## Decision and boundary

`OntologyRuntimeObservationFacts` owns the publication bookkeeping shared by
water occupancy, actor water presence, and support-contact sensors. It only
retracts facts that the same sensor successfully published.

`OntologyWorldBootstrap.WorldRebuilt` is now distinct from `WorldChanged`.
The former means the runtime world instance was replaced by reset or restore;
the latter still means any world/inference update. Observation adapters use
`WorldRebuilt` to republish into a new runtime world. Presentation adapters can
continue using `WorldChanged` to refresh visuals.

This change does not create a new rule, profile, or object-name exception.
It does not make Unity own swimming, buoyancy, or equipment permission.

## Ontology expression

- Observed relations: `occupies`, `immersion_depth`, `supported_by`
- Inferred/presentation outcomes remain owned by existing rules and adapters.
- `Swimming`, `Moving`, `Idle`, `Shallow`, and `Deep` use canonical term
  constants instead of duplicated string literals in swimming adapters.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Observation added/removed | The sensor publishes and later retracts its own relation. | `OntologyWaterOccupancySensorTests` PlayMode: 3/3 passed |
| Authored fact already exists | A runtime sensor does not take ownership or remove it. | `OntologyWorldStateTests` EditMode: 4/4 passed |
| Physics/equipment regression | Buoyancy, attachment, and support tests remain green. | PlayMode: 4/4 passed |

## Performance and multiplayer impact

- The shared synchronizer uses per-sensor reusable removal buffers: no new
  collection allocation is required on normal observation refreshes.
- It adds no database writes, network traffic, authority commands, or zone
  fan-out. These facts remain local ephemeral runtime observations.

## Documentation updated

- [x] Change record (English and Korean)
- [x] Harness scenario manifest
- [ ] `PROJECT_CONTEXT.md` / `PROJECT_CONTEXT.ko.md` (no product-policy change)
- [ ] Localization CSV / migration (canonical IDs unchanged)
