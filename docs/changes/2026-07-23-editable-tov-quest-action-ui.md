# Editable TOV quests and actions UI

## Summary

- **Date:** 2026-07-23
- **Status:** Implemented
- **Classification:** Unity presentation

## Intent and boundaries

`QuestActionPanel` now presents generated quests and currently available
ontology actions in the approved TOV visual style. The main panel, headers,
scroll views, selected-quest summary, quest-row template, action-row template,
footer, close button, and external toggle button are authored as separate uGUI
hierarchy objects so designers can adjust them directly in the Unity Editor.

Editor-only sample rows make the final layout visible without entering Play
Mode. `OntologyQuestActionPanel` disables those samples in `Awake` and clones
only the authored hidden templates for real runtime data.

This change does not create quests, action candidates, rules, or Facts. Unity
continues to present results returned by the ontology runtime or World
Authority.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Editor authoring | All major visual elements are individually editable in the hierarchy | `quest_action_tov_editable_final_verified.png` |
| Runtime data | Real quest/action rows are cloned from the hidden authored templates | `OntologyQuestActionPanel` |
| Removed preview behavior | Editor sample rows are inactive in Play Mode | `TormiaMainSceneSmokeTests` |
| Localization | New section headings, prompt, and empty-selection labels resolve in English and Korean | Localization CSV entries |

## Alignment refinement

- The header now follows the shared TOV account-panel pattern: logo, centered
  title, subtitle, and an independently editable close button.
- Quest-row progress tracks and status buttons have separate layout regions, so
  neither element overlaps the other.
- Runtime progress is derived from completed quest goals instead of a visual
  placeholder, and the detail percentage is updated from the same value.

## Performance and authority impact

The panel rebuilds rows only when refreshed, as before. No per-frame Authority
request, durable world write, or new gameplay fallback was introduced.
