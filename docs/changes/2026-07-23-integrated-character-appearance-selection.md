# Integrated character appearance selection

## Summary

- **Date:** 2026-07-23
- **Status:** Implemented and verified
- **Classification:** Unity presentation and account-flow navigation

## Intent and boundaries

`AccountCharacterSelectionPanel` now owns the final read-only appearance review
for an existing account character. Its selected-character detail area presents
the full-body preview and equipped appearance slots before the player continues.

The separate `AccountAppearanceReviewPanel` is removed from the active scene and
onboarding route. Selecting Continue now moves directly from character selection
to world selection. The compatibility method `ShowAppearanceReview()` redirects
to world selection so stale scene event bindings cannot reopen the removed step.

This does not change account-owned appearance data, world Facts, or authority
commands. It only removes a duplicated presentation step.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Integrated flow | Character selection shows the selected character and equipped part icons | `character_selection_v2_final_verified.png` |
| Continue | Continue moves directly to world selection | `OntologyAccountCharacterSelectionPanel` binds Continue to `ShowWorldSelection()` |
| Removed behavior | The standalone appearance panel never becomes visible | `AppearanceRouteSkipsRemovedPanelAndOpensWorldSelection` |
| Compatibility | Calling the former appearance route still proceeds to world selection | `ShowAppearanceReview()` delegates to `ShowWorldSelection()` |
| Regression | Account navigation tests and development harness remain green | Relevant PlayMode suite and `verify-development.ps1` |

## Performance and authority impact

No polling, database write, durable world event, or new authority call was
introduced. One redundant UI transition and preview studio were removed.
