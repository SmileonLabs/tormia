# Jua project typography

## Summary

- **Date:** 2026-07-30
- **Status:** Implemented
- **Scope:** Project-owned Unity UI presentation

## Intent

Use one readable TOV game font across authored and runtime-created UI while
making player input and selection text slightly more prominent.

## Decision

The project creates a dynamic TextMesh Pro asset from
`Assets/UI/Fonts/Jua-Regular.ttf` and assigns it as the TMP project default.
All TMP text in `TormiaBootstrap`, `TormiaUI`, `TormiaWorld`, and
`Assets/Prefabs/Ontology/UI` and `Assets/Data/Ontology/UI` use that asset.

Text below a Unity `Selectable` gains 2 points when migrated. Other labels keep
their current size. The migration changes no hierarchy, layout, transform,
alignment, or color values and excludes third-party assets. It increases a
component only while changing its font, so running it again is safe.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Authored project UI | Every TMP component uses Jua | `ProjectOwnedUiAssetsUseJua` |
| Interactive text | Font size increases by exactly 2 points | `InteractiveTextGetsTwoPointIncreaseExactlyOnce` |
| Re-run | A second migration makes no further size change | `InteractiveTextGetsTwoPointIncreaseExactlyOnce` |
| Non-interactive label | Existing point size is preserved | `NonInteractiveTextKeepsItsExistingPointSize` |

## Updated documents

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
