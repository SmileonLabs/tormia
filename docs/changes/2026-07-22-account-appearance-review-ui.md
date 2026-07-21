# Account appearance review UI

## Summary

- **Decision:** Separate appearance confirmation from the account profile-ontology
  review in the development entry flow.
- **Scope:** Unity presentation over existing account-owned character data.

## Flow

```text
development account -> character selection -> appearance review
-> world selection -> profile ontology review -> world entry
```

`OntologyAccountAppearanceReviewPanel` reads only the selected account
character's name, template, and equipped part IDs. It never writes World Facts
or creates visual controls at runtime. The panel is an authored hierarchy root
under `OntologyGameCanvas`, duplicated from the existing Farm UI styled panel,
so designers can adjust its layout directly in Unity.

## Verification

- The navigator opened the new appearance-review panel in Play Mode.
- The authored scene now serializes the appearance panel, account-entry flow,
  CanvasGroup, summary label, navigation buttons, and navigator references;
  runtime discovery is no longer required for this step in `TormiaMain`.
- `OntologyAccountFlowNavigatorTests.AppearanceStepOpensOnlyWhenPanelIsPresent`
  passed in Play Mode, covering both the present and removed-panel cases.
- `CompleteReviewSequenceKeepsExactlyOneStepVisible` covers account, character,
  appearance, world, profile, and completed-entry panel transitions.
- All three character cards and all three world cards now serialize their card,
  button, and primary-label bindings in `TormiaMain`.
- `scripts/verify-development.ps1 -SkipServerBuild` passed.
- Unity Console was clean immediately after compiling the panel and navigator
  changes.
