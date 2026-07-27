# Editable TOV account creation presentation

## Summary

- **Date:** 2026-07-23
- **Status:** Implemented and runtime-verified
- **Classification:** Unity presentation and localized labels

## Intent and boundaries

The real registration panel now matches the approved TOV login visual system.
The logo, frame layers, card, labels, inputs, and button surfaces remain separate
scene-authored uGUI objects. Authentication continues through the existing
World Authority registration flow; no account data was moved into world Facts.
Runtime code updates text and interaction state but does not own layout.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled | Email, display name, and password fields display in the TOV card | `account_creation_tov_editable_runtime.png` |
| Localization | All three placeholders use the active language pack | Runtime reported `name@example.com`, `게임에서 사용할 이름`, `10자 이상의 비밀번호` |
| Navigation | Back returns to login without changing registration behavior | Runtime reported `creation=0, login=1`; reopening restored creation to `1` |
| Editable | Visual pieces remain independently selectable | Separate frame, surface, logo, labels, inputs, and standard `Button` objects saved in `TormiaMain` |
| Removed fallback | The unrelated appearance button is absent | `CustomizeAppearanceButton` removed from `AccountCreationPanel` |
| Regression | Existing ontology suite and development harness remain green | EditMode 53/53; `verify-development.ps1` passed including the World Authority Docker build |

## Performance and authority impact

Presentation-only. No polling, durable world event, database write, realtime
traffic, or authority rule was added.
