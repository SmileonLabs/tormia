# Unified World Placement UI

## Decision

Runtime placement uses one editable TOV panel with explicit `Object`, `NPC`,
and `Monster` modes. `OntologyPlaceableDefinition.placementKind` owns the
catalog mode; Unity never derives it from prefab, mesh, or object names.

## Presentation

- Top-level mode tabs and category rows use separate selected/unselected
  sprites.
- Category and ontology-data regions scroll without visible scrollbars.
- The legacy status row and bottom close button are removed.
- Closing is available through the editable top-right icon and `Esc`.
- The hierarchy remains authored in `ObjectPlacementHUD.prefab`; runtime code
  only binds data, callbacks, and selection state.

## Verification

- Prefab structure and removed legacy controls are covered by
  `OntologyWorldPlacementUiTests`.
- Existing catalog entries default to `Object`, preserving saved content while
  allowing authored NPC and monster entries later.
