# World Edit UI prefab authoring

## Summary

- **Date:** 2026-07-20
- **Owner:** Codex / project team
- **Status:** Verified
- **Related request:** Make every World Edit UI element directly adjustable in
  hierarchy/prefab authoring, without duplicate script-built UI.

## Intent

Allow a designer to tune World Edit layout, typography, sprites, button hit
areas, and dropdown templates directly in Unity while retaining dynamic lists
for authored ontology data.

## Data classification

- [x] Unity presentation

## Decision and boundary

`WorldEditHUD.prefab` owns the ontology editor's static hierarchy, including
the triple/rule/result row templates and physical-detail popup.
`WorldEditContextHandle.prefab` owns the selected-object toolbar hierarchy.
Runtime binders may bind data and duplicate an authored row template for an
arbitrary number of records; they do not create visual controls or layout.

The legacy editor builder that recreated `WorldEditHUD` was removed. The setup
tool now requires the prefab instance rather than silently generating a second
UI tree.

## Ontology expression

None. This is Unity presentation authoring only; facts, profiles, rule blocks,
and inference remain unchanged.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Prefab assets | HUD and contextual handle are inspectable/editable | Both prefab hierarchies were read through Unity MCP |
| Runtime entry | Existing editor starts without missing bindings | Entered Play Mode; Unity Console had no errors/warnings |
| Duplicate generator removed | Setup cannot regenerate a second HUD | `FarmWorldEditUiBuilder` removed; setup reports a missing prefab instance |

## Performance and multiplayer impact

No new polling, allocation pattern, database write, authority, or Zone impact.
Rows are still instantiated only in proportion to visible authored data.

## Documentation updated

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [ ] Localization CSV / migration, not needed
- [ ] Test scenario manifest, not needed
