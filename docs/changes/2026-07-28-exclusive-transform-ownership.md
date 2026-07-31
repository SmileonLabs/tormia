# Exclusive Transform Ownership and Stable Placement

## Summary

- **Date:** 2026-07-28
- **Status:** Implemented and verified in connected Unity Editor
- **Request:** Remove first-step avatar sinking and repeated bouncing of placed objects without object-name patches.

## Intent

Give every Unity Transform one explicit presentation owner, align placement
against the final ontology-selected collision shape, and preserve only coarse
settled physics poses through the existing Authority command boundary.

## Data classification

- Durable world data: accepted `place_entity` and coarse `move_entity` commands
- Runtime observation: Rigidbody velocity/sleep and support contact
- Unity presentation: Transform ownership, collision settling, avatar grounding

## Decision and boundary

`WorldEditor > Attachment > LocalController/ActorRuntime > RuntimePhysics >
DurableProjection` is the exclusive presentation priority. Dynamic objects are
seeded once from Authority. Active physics is not overwritten by projection
refreshes and publishes no per-frame events. Catalog definitions must explicitly
select physical profiles; missing data creates no implicit `HeavySinking`
behavior.

## Ontology expression

The owner is derived from `physical_profile`, attachment relations, and active
presentation adapters. `AuthorityKinematic` is a canonical mobility value for
actors whose Unity Rigidbody must remain kinematic. No prefab or object name is
used.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled | Dynamic projection seeds once, then yields to Rigidbody; final collider is support-aligned | `OntologyPlayerInputPriorityTests`, `OntologyRuntimeDataAssetTests` |
| Disabled | Removing Dynamic or attachment ownership returns the Transform to durable projection; missing profile creates no physics | same suites |
| Entry | Capsule-safe grounding clears vertical velocity without writing durable data | `EntryGrounding_ClearsOnlyEphemeralVerticalVelocity` |
| First move | The first `CharacterController.Move` after entry grounding stays on the resolved support surface | `EntryGroundingKeepsFirstMoveOnSupportSurface` |

Connected-editor verification on 2026-07-29 passed 48 targeted EditMode
tests and 18 targeted PlayMode tests. The PlayMode set includes attachment
ownership, physical-profile synchronization, first-move grounding, and
separated-scene composition. Kinematic bodies are no longer assigned linear or
angular velocity while Unity physics ownership is suspended.

## Performance and multiplayer impact

Physics stays local presentation. A settled body may issue one revisioned,
idempotent move command after a meaningful change and cooldown. No per-frame
database or network writes are introduced.

## Updated documents

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
