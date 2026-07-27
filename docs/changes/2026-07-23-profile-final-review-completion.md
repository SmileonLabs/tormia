# Profile final review completion

## Summary

- **Date:** 2026-07-23
- **Status:** Implemented and verified
- **Classification:** Account profile presentation, Authority-derived world permission, and Unity navigation

## Intent and boundaries

`AccountProfileReviewPanel` is the final confirmation step before world entry.
It now presents the selected character, equipped appearance, selected world
title, account profile relations, Authority-provided world role, edit access,
and entry readiness.

World permission is read from `SelectedWorldRole` and
`CanEditSelectedWorld`. The UI does not infer or grant permission. Owner and
editor roles show that world editing is enabled; viewer shows read-only access.
A missing role prevents the Enter World button from becoming interactable.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Owner/editor | Localized role and world-edit permission are visible; entry is enabled | `ProfileFinalReviewShowsAuthorityRoleAndRequiresWorldPermission` |
| Viewer | Viewer and read-only are visible; world entry remains allowed | `ProfileFinalReviewShowsAuthorityRoleAndRequiresWorldPermission` |
| Missing permission | No-permission status is visible and entry is disabled | `ProfileFinalReviewShowsAuthorityRoleAndRequiresWorldPermission` |
| Equipped appearance | Four hierarchy-authored slots are serialized and bind from `CharacterPartDatabase` | `AccountProfileReviewPanel.appearanceSlots` contains four non-null references |
| Navigation | World selection opens profile review; Enter proceeds to loading | `OntologyAccountFlowNavigatorTests` |
| Loading | Progress fill remains visually stable while advancing | `OntologyWorldEntryLoadingPanelTests` |
| Regression | Development harness and World Authority Docker build pass | `scripts/verify-development.ps1` |

## Performance and authority impact

The panel reads the account dashboard already returned by World Authority. No
additional request, polling, durable event, database write, or permission
fallback was added.
