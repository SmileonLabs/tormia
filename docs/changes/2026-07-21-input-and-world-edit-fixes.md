# Input, world-edit, and ontology animation fixes

## Summary

- **Date:** 2026-07-21
- **Status:** Implemented and verified
- **Scope:** Unity presentation/input adapters and world-edit interaction

## Decisions

1. The authored world-edit handle remains the only runtime toolbar. Its ontology
   button restores the selected placeable from the handle target before opening
   the editor; it does not delete or replace the object.
2. Attachment detach requires both the ontology-defined interaction volume and
   a ray hit on the visible renderer bounds. A blank area of a large profile
   volume is not enough to detach.
3. Toolbar buttons keep their Button component on the hierarchy-authored
   object, while the visible icon Graphic is the raycast target. Moving an icon
   in the hierarchy therefore moves its click area with it.
4. The Input System player controller is the single movement/animation input
   source. Legacy `MovePlayerInput` and `CharacterMover` components are disabled
   on the player to prevent conflicting axis/animation updates.
5. Jump is an ephemeral input signal applied through `CharacterController` and
   configured gravity/jump-height fields. The animation adapter only presents
   the resolved ontology intent; no mesh or object name is used as a rule.

## Verification

| Case | Result | Evidence |
| --- | --- | --- |
| Ontology icon with selected placeable | Pass | `OpenOntologyEditorFor` reselects and opens the existing editor |
| Detach click outside visible attachment | Pass | `OntologyAttachmentAdapter.RaycastPresentation` bounds gate |
| Horizontal/vertical movement animation | Pass | Input System axis drives animator parameters; legacy controllers disabled |
| Jump with gravity | Pass | CharacterController vertical velocity and fallback gravity path |
| Unity console after compile | Pass | 0 error/warning entries before test runner output |
| Development harness | Pass | `verify-development.ps1 -SkipServerBuild` |

The full EditMode suite still reports five existing failures in character-part
and buoyancy semantic tests; they are unrelated to this change and require a
separate data/test-isolation task.
