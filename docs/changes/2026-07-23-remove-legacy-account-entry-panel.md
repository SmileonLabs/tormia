# Remove legacy combined account-entry panel

## Summary

- **Date:** 2026-07-23
- **Status:** Implemented
- **Classification:** Unity presentation and account-flow navigation

## Intent and boundaries

The onboarding flow now uses dedicated panels for login, registration,
character creation/selection, world creation/selection, profile review, and
world-entry loading. `OntologyAccountEntryPanel` duplicated those
responsibilities and retained a direct world-entry path that could bypass the
current review and loading sequence, so it is removed from the active scene,
runtime code, and navigation tests.

Character-creation Back behavior is now explicit:

- an account with at least one character returns to character selection;
- an account with no character signs out and returns to login.

This change affects only Unity presentation and navigation. It does not move
account-owned data into world Facts and does not add an Authority command.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Existing account | Back from character creation returns to character selection | `CharacterCreationBackReturnsExistingAccountToCharacterSelection` |
| Empty account | Back signs out and returns to login | `CharacterCreationBackSignsOutEmptyAccountAndReturnsToLogin` |
| Removed behavior | No active-scene object, component, navigator field, or route references the combined panel | Scene/component search and source reference scan |
| Regression | Current account navigation and development harness remain green | Four navigator PlayMode tests and `verify-development.ps1 -RequireServices` |
| Harness integrity | A batch Unity launch without a result file cannot report a false pass | `run-unity-harness-tests.ps1` now checks the process exit code and parsed result XML |

## Performance and authority impact

No polling, database write, durable world event, or new Authority call was
introduced. The empty-account Back path uses the existing logout operation.
