# Explicit Collision Layers and Legacy Motion Removal

## Summary

- **Date:** 2026-07-31
- **Owner:** TOV development harness
- **Status:** Implemented; Unity Editor execution pending
- **Request:** Stabilize the local collision foundation before extending movement to complete server authority.

## Intent

Make local character collision ownership explicit and reusable. Remove dormant
legacy movement components, map semantic collision roles to project-owned Unity
Physics Layers, and require support surfaces to declare their meaning.

## Data classification

- [ ] Account profile
- [x] Durable authored world data
- [x] Runtime observation
- [ ] Inferred state
- [x] Unity presentation
- [x] Transport / authority / infrastructure

## Decisions and boundaries

`OntologyCollisionRole` in Physical Meaning is the semantic source. The Unity
Physics Layer is an authored presentation mapping and never becomes an
ontology identifier or gameplay permission. `OntologyCharacterMotionCoordinator`
is the only normal local CharacterController movement owner. This milestone
does not move collision simulation to World Authority.

## Ontology representation

The existing `physical_profile` relation selects a profile whose
`motionDriver`, `collisionRole`, and motion tuning derive Unity adapters.
Removing the Physical Meaning restores the original Unity layer and disables
the movement lease. Static-collider shape alone does not imply
`WalkableSupport`.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled / added | Every collision role maps to one existing project layer and local character meaning applies ActorBody | `OntologyRuntimeDataAssetTests.StoneCatalogUsesNonBuoyantDynamicPhysicalProfile`; `OntologySemanticAdapterSynchronizerTests.LocalCharacterPhysicalMeaningOwnsAndRemovesMotionLease` |
| Disabled / removed | Profile removal restores the original layer and removes the local motion lease | `OntologySemanticAdapterSynchronizerTests.LocalCharacterPhysicalMeaningOwnsAndRemovesMotionLease` |
| Regression / exception | An unauthored static collider is not treated as walkable support | `OntologyHybridCharacterMotionTests.UnauthoredStaticColliderIsNotImplicitWalkableSupport` |

## Performance and multiplayer impact

The collision matrix is applied once from the profile database. Layer assignment
occurs only during semantic synchronization and does not create per-frame
allocations, database writes, or revisions. Movement remains client collision
presentation with sequence- and Zone-bounded Authority relay until a later
server fixed-tick collision solver is implemented.

## Updated documentation

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [ ] Language-pack CSV / migration, if required
- [x] Harness scenario list, if required
