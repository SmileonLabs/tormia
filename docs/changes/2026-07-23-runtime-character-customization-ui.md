# Runtime character customization UI

## Summary

- **Date:** 2026-07-23
- **Status:** Implemented
- **Classification:** Account profile + runtime observation + Unity presentation

## Decision

The in-world `OntologyCharacterCustomizationPanel` remains separate from
`AccountCharacterAppearancePanel`. Only the in-world panel owns the runtime
C-key and `ToggleHint` button. Both panels reuse the same category icons, round
part cards, and live 3D preview presentation, but they retain separate
navigation and lifecycle rules.

Part selection immediately updates the local adapter and its projected
`equipped_part` facts. Closing the in-world panel saves the selected part IDs
to the currently selected account character through World Authority. The
account profile remains the durable owner; no new durable world Fact is used
to remember the preference.

## Enabled and removed cases

| Case | Expected result |
| --- | --- |
| Runtime panel enabled | C-key/open button opens only the in-world panel; part clicks equip immediately; close persists the account appearance |
| Account appearance step active | It opens only through account-flow navigation and does not consume the runtime C-key/open button |
| Runtime panel removed or disabled | No hidden account-panel input fallback opens the appearance UI |

## Verification

- Scene bindings and hierarchy are checked by `TormiaMainSceneSmokeTests`.
- Equipment conflicts and linked costume behavior are checked by
  `OntologyCharacterPartAdapterTests`.
- `scripts/verify-development.ps1 -RequireServices` verifies the shared
  harness and World Authority services.
