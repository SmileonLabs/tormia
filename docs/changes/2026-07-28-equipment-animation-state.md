# Equipment animation state from Authority projection

## Summary

- **Date:** 2026-07-28
- **Status:** Implemented and verified
- **Scope:** World Authority projection, Unity animation presentation, combat data

## Intent

An equipped actor must use the stationary and moving poses declared by the
equipped item's presentation data. Runtime code must not choose `WeaponIdle`
or `WeaponWalk` from a weapon, prefab, mesh, or GameObject name.

## Ownership decision

- `equipped_item` remains durable, revisioned World Authority data.
- The equipped entity's projected `templateId` identifies its explicit
  `CombatCatalog` presentation definition.
- The selected idle/move intent and Animator/Playable state are ephemeral
  Unity presentation.
- No duplicate durable animation Fact is written for stationary equipment
  presentation.

## Enabled and removed behavior

| Case | Expected result |
| --- | --- |
| Actor has one `equipped_item`; entity template has an explicit idle intent | The actor loops that intent while stationary |
| The same definition has an explicit move intent and the actor moves | The actor loops `WeaponeWalk.fbx` through the `WeaponWalk` intent |
| A confirmed transient action plays | The action temporarily overrides the pose, then returns to the equipment idle/walk selected by current movement |
| Equipment relation, catalog entry, or idle/move intent is removed | No hidden weapon animation fallback; matching controller Idle/Locomotion returns |
| Projection has conflicting equipped entities | No arbitrary equipment pose is selected |

## Data and versioning

The development combat package advances to `1.3.0`. `equip_weapon` definition
version 3 declares `presentation.actorAnimationIntent=WeaponEquip`; the durable
effect remains the Authority-owned `equipped_item` relation.

## Verification

- `OntologyCombatVerticalSliceAssetTests`: 16/16 passed, including enabled,
  removed, loopable idle, and Humanoid `WeaponWalk` cases.
- `OntologyAttachmentAdapterTests`: 3/3 passed in PlayMode.
- Unity Console: no errors after compilation and the runtime startup check.
- `scripts/verify-development.ps1`: passed, including the Docker World
  Authority build.
- The signed-out runtime check correctly left the player presentation inactive;
  authenticated end-to-end visual confirmation remains a manual gameplay check.

## Updated documents

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
