# Editable TOV character creation presentation

## Summary

- **Date:** 2026-07-23
- **Status:** Implemented and runtime-verified
- **Classification:** Unity presentation and localized labels

## Intent and boundaries

The account character creator now uses the approved wide TOV onboarding design.
The 13-category rail, circular part grid, live preview, name field, and navigation
buttons remain backed by the existing scene-authored/runtime adapter contracts.
Part choice still changes account-owned appearance through the character part
adapter; the UI does not create a second gameplay or ontology rule.

All visual layers and templates are independently editable in the hierarchy.
Runtime rebuilding populates only category and part instances from the existing
database. It does not reposition the authored areas.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled | TOV frame, logo, category rail, circular thumbnails, large live preview, name field and buttons display | `character_creation_tov_editable_runtime_final.png` |
| Categories | All 13 categories are generated in the vertical scroll rail with no visible scrollbar | Runtime hierarchy returned the 13 canonical category buttons |
| Part selection | Clicking a thumbnail immediately toggles the selected part | Runtime click returned `해제함: 남성 헤어 001` |
| Navigation | Back hides both creation and embedded appearance layers; reopening restores both | Runtime returned `creation=0, appearance=0`, then `1/1` |
| Editor preview | The character screen can be isolated without rebuilding layout | `Tormia/UI/Preview/Character Creation Panel`; 8 category and 16 part dummy objects are hierarchy-authored |
| Removed behavior | Separate equip/unequip detail controls remain hidden in embedded creation | Thumbnail selection is the only embedded equip interaction |
| Regression | Unity suites and development harness remain green | EditMode 53/53, PlayMode 34/34, and `verify-development.ps1` including the World Authority Docker build passed |

## Performance and authority impact

Presentation-only apart from existing part-selection calls. No polling, durable
world event, database write per frame, realtime traffic, or authority rule was
added.
