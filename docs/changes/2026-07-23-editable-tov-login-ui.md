# Editable TOV login presentation

## Summary

- **Date:** 2026-07-23
- **Status:** Implemented and runtime-verified
- **Classification:** Unity presentation and localized labels

## Intent

Apply the approved TOV-branded login design to the scene while keeping every
visual layer directly editable in the Unity hierarchy.

## Decision and boundaries

- `AccountLoginPanel` remains the existing authentication presenter; the World Authority login and account-creation navigation contracts are unchanged.
- The logo, blue frame, gold frame, card surface, title, labels, inputs, and button surfaces are separate uGUI objects rather than one flattened mockup image.
- Layout is scene-authored. Runtime code updates localized text and interaction state only and does not recreate or reposition these controls.
- Login and account creation remain account authentication data, not ontology Facts or world data.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled | Login card displays the imported TOV logo and localized fields | `account_login_tov_editable_runtime.png` |
| Navigation | Create Account hides login and opens account creation | Runtime invocation returned `login=0, creation=1`; returning restored login to `1` |
| Editable | Every design layer is selectable in the hierarchy | Scene hierarchy contains `BrandFrameBlue`, `BrandFrameGold`, `CardSurface`, `TovLogo`, field objects, and standard `Button` components |
| Removed fallback | Legacy display-name and appearance controls are absent from login | `CustomizeAppearanceButton` and `DisplayNameInput` removed from `AccountLoginPanel` |
| Regression | Existing ontology EditMode suite and development harness remain green | EditMode 53/53; `verify-development.ps1` passed including World Authority Docker build |

## Performance and authority impact

Presentation-only. No polling, world events, database writes, realtime traffic,
or authority behavior was added.
