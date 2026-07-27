# Creator Workspace Specialist NPCs

## Summary

- **Date:** 2026-07-24
- **Owner:** Tormia team
- **Status:** Verified initial presentation slice
- **Related request:** Populate temporary creator assistants from reusable player character parts

## Intent

Make the future prompt-driven production pipeline understandable inside the
world by placing distinct temporary specialist characters that can later be
replaced without changing authored world data.

## Data classification

- [ ] Account profile
- [ ] Durable authored world data
- [ ] Runtime observation
- [ ] Inferred state
- [x] Unity presentation
- [x] Transport / authority / infrastructure

## Decision and boundary

The eight specialist characters are children of an owner-only creator workspace
presentation root. Their appearances reuse the character part catalog but do
not use the player part adapter, publish equipped-part Facts, or register
ordinary world entities. The runtime gate requires confirmed world entry,
creator mode, and the canonical `owner` role.

## Ontology expression

Canonical service IDs describe responsibility: `WorldArchitect`,
`ResourceMaker`, `OntologySteward`, `PhysicsEngineer`, `RuleEngineer`,
`QuestDesigner`, `UiDesigner`, and `QaPublisher`. Korean names are display-only.
Future service outputs remain subject to normal account or revisioned World
Authority contracts.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled / added | Owner in an entered world can see eight distinct part-based assistants | `TormiaMain.unity`; `CreatorWorkspaceNPCsFinalView.png` |
| Disabled / removed | Signed-out, not-entered, creator-mode-off, editor, and viewer states hide the workspace | `OntologyCreatorWorkspaceControllerTests` |
| Regression / edge case | Appearance projection creates no ontology actor or world Fact | NPC hierarchy contains only presentation component and cloned visual |

## Performance and multiplayer impact

The initial slice has no polling, database writes, Authority commands, or
network traffic. Eight visuals are instantiated in the authored scene and
gated as one root.

## Documentation updated

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [ ] Localization CSV / migration, if needed
- [x] Test scenario evidence
