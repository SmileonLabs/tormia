# Mobile gameplay input

## Decision

Android and iOS gameplay use the project-wide Input System action asset as the
single device-binding source. Unity input remains a trigger adapter: movement,
jump, equipment and primary pointer input continue through the existing
canonical intent, Rule Block and World Authority boundaries.

## Implementation

- `OntologyInputSystemPlayerInput` clones project-wide actions instead of
  rebuilding keyboard-only actions at runtime.
- The project-owned mobile controls prefab emits virtual Gamepad controls for
  movement, jump and equipment. It is visible only while the session is
  `InWorld`, respects the landscape safe area, and remains editable in
  `TormiaUI`.
- Gameplay control touches consume world pointer clicks but do not count as a
  blocking modal UI surface. Other UI continues to stop gameplay input.
- No mobile control applies motion, equipment, damage or another gameplay
  result directly.

## Verification

Tests cover required touch/gamepad bindings, prefab control paths and landscape
safe-area conversion. Existing Rule-Block-removed behavior remains unchanged.
