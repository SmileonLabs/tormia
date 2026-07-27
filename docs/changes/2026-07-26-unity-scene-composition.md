# Unity Bootstrap, World, and UI Scene Composition

## Summary

- **Date:** 2026-07-26
- **Owner:** Tormia team
- **Status:** Implemented and verified
- **Related request:** Separate editable UI from the world scene for maintainability

## Intent

Keep UI authoring independent from 3D world authoring while preserving the
existing account-to-world flow, ontology ownership boundaries, and editable
Unity hierarchy.

## Data classification

- [ ] Account profile
- [ ] Durable authored world data
- [ ] Runtime observation
- [ ] Inferred state
- [x] Unity presentation
- [x] Transport / authority / infrastructure

## Decision and boundary

The build enters through `TormiaBootstrap`. Persistent services and the
`EventSystem` live there. `TormiaWorld` and `TormiaUI` load additively and are
the authoritative editable scenes for 3D and UI presentation respectively.
The former `TormiaMain` integration snapshot is archived under
`Assets/Scenes/Legacy` and is not an authoring, test, or build scene.

The former editor migration command was removed. Separated scenes are no
longer regenerated from the snapshot because that could overwrite direct
world and UI authoring.

Scene separation does not move account data into world Facts and does not move
gameplay rules into UI. The loader waits for additive activation, refreshes
cross-scene controller bindings, and rebuilds the local authored projection.

## Ontology expression

No canonical predicates, profiles, rules, rule blocks, or Fact origins change.
The scene boundary is a Unity presentation/deployment boundary only.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled / added | Bootstrap loads separate World and UI scenes and authored Facts are available | `TormiaSceneCompositionSmokeTests.BootstrapLoadsWorldAndUiAsSeparateScenes`; `SceneCompositionSignedOutFinal.png` |
| Disabled / removed | Signed-out users still cannot see world HUD or runtime presentation | Same smoke test plus existing runtime-gate tests |
| Regression / edge case | Exactly one EventSystem and AudioListener exist after composition | Same smoke test |

## Performance and multiplayer impact

The two additive scenes load once during startup. Per-frame input and
observations are unchanged. Authority remains the only durable shared-world
write boundary.

## Documentation updated

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [ ] Localization CSV / migration (not required)
- [x] Test scenario evidence
