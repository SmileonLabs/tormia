# Single Authoritative Unity UI Scene

## Summary

- **Date:** 2026-07-26
- **Owner:** TOV team
- **Status:** Implemented
- **Related request:** Remove duplicate UI ownership from `TormiaMain`

## Intent

Make `TormiaUI` the single editable source for account and in-world UI while
preserving the separated Bootstrap/World/UI runtime composition.

## Data classification

- [ ] Account profile
- [ ] Durable authored world data
- [ ] Runtime observation
- [ ] Inferred state
- [x] Unity presentation
- [ ] Transport / authority / infrastructure

## Decision and boundary

`TormiaUI` is the only UI authoring scene. The empty legacy
`FarmUI_DemoCanvas` and its old `MainMenuController` behavior were removed from
that scene. The all-in-one `TormiaMain` scene was moved to
`Assets/Scenes/Legacy/TormiaMain.unity`, and the destructive tool that rebuilt
separated scenes from it was removed.

`TormiaBootstrap`, `TormiaWorld`, and `TormiaUI` remain the build composition.
The archived scene is retained only as recoverable historical material and
must not regain build, test, or authoring authority.

## Ontology expression

No ontology terms, Facts, rules, profiles, or Authority commands change. This
is strictly a Unity presentation ownership cleanup.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled / added | Bootstrap loads the authoritative `TormiaUI` hierarchy | `TormiaUiSceneSmokeTests` |
| Disabled / removed | No `FarmUI_DemoCanvas` exists in the composed runtime | `TormiaUiSceneSmokeTests.CompositionCreatesOntologyWorldAndUsesSingleUiScene` |
| Regression / edge case | Account, quest, customization, HUD, and toast bindings still resolve | Same PlayMode suite and existing scene-composition tests |

## Performance and multiplayer impact

One empty Canvas, raycaster, and legacy controller are removed. There is no
network, database, Authority, or ontology runtime impact.

## Documentation updated

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [ ] Localization CSV / migration (not required)
- [x] Relevant tests
