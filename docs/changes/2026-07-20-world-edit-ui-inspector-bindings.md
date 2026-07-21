# World Edit UI inspector bindings

## Decision

The hierarchy-authored World Edit UI no longer discovers its root controls by
`Transform.Find(...)` while the game is running. `OntologyRuntimeWorldFactEditorPanel`
and `OntologyRuntimeWorldEditorPanel` use serialized references stored on
`OntologyGameCanvas` instead.

## Why

World Edit is intentionally edited in the Unity hierarchy with the Farm UI kit.
Runtime name-path discovery made a harmless rename or layout change silently
break a control, and made the Inspector misleading because the saved references
were overwritten each time the panel bound itself.

## Migration and scope

Both panels expose a one-time **Migrate Legacy Hierarchy Bindings** context-menu
action. It exists only in the Unity Editor to populate the serialized fields for
older scenes. Runtime code does not call it. The current `TormiaMain` scene has
been migrated and saved.

This change covers the static panel/root controls. The reusable row templates
still locate their own internal child controls; those templates are the next
separate migration target.

## Verification

- All 24 World Edit references on `OntologyRuntimeWorldFactEditorPanel` are assigned.
- Unity Console: no project code errors after compilation.
- `scripts/verify-development.ps1 -SkipServerBuild`: passed.
- `OntologyWorldStateTests`: 4/4 EditMode tests passed.

