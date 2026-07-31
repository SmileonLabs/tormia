# Prefab-authored attachment grip

## Summary

- **Date:** 2026-07-28
- **Status:** Implemented and verified
- **Scope:** Unity attachment presentation and editor authoring

## Intent

Remove weapon-mesh correction values from the shared right-hand attachment
profile. Let each project-owned weapon prefab declare the exact point and
orientation that must meet the actor's hand socket.

## Ownership decision

- Authority facts still decide whether an item is equipped.
- The attachment profile selects a stable actor socket id and optional
  socket adjustment.
- `OntologyAttachmentSocket` is project-owned actor presentation data. It
  follows an explicitly configured rig bone or prop anchor without modifying
  the imported character prefab.
- `OntologyAttachmentGripPoint` on an item prefab owns model-specific handle
  alignment.
- The generic attachment adapter aligns those two presentation contracts.
- No weapon ID, prefab name, or mesh name is inspected by gameplay code.

## Enabled and removed cases

| Case | Expected result |
| --- | --- |
| Item prefab has a grip point | Grip point position and rotation match the actor socket |
| Required weapon grip point is removed | Weapon attachment presentation is disabled without a hidden fallback |
| Optional legacy grip point is removed | The explicit generic profile pose remains available for non-weapon compatibility |
| Authored socket is present | The item attaches to that socket instead of raw Humanoid bone axes |
| Equip relation is removed | World transform and physics ownership return to the item |

## Authoring workflow

Open the right-hand attachment preview, move and rotate
`EquippedWeaponPreview`, then use **Save Current Pose To Prefab Grip Point**.
The shared profile is not modified.

The actor-side pose is edited at
`OntologyPlayer/AttachmentSockets/RightHandWeaponSocket`. Move or rotate that
object and use **Capture Current Transform As Socket Pose**. The socket follows
the imported rig's `RightHand/RightHandProp` anchor at runtime.

## Verification

- PlayMode attachment tests cover both required marker-present and
  required marker-removed paths, plus the optional legacy profile path.
- The actual `TormiaWorld` player and `Sword08Corrupted` runtime integration
  resolves `RightHandWeaponSocket`, parents the weapon there, and reports zero
  position and rotation error.
- The three project-owned sword wrappers contain editable grip-point children.
- Development harness and Unity Console are checked after the change.

## Updated documents

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
