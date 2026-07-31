# Granular removal of meaning-package Rule Blocks

## Summary

- **Date:** 2026-07-31
- **Status:** Implemented; verification recorded by the development harness
- **Scope:** World Authority meaning-package ownership, runtime editor fallback,
  typed Triple ownership, and Rule Block removal

## Problem

Removing one Rule Block owned by a meaning package removed every sibling Rule
Block. At the same time, template baselines claimed only canonical Facts, so
number and boolean Triples such as health and tuning remained after the package
was removed.

## Decision and boundaries

- A package-owned Rule Block is independently removable.
- Removing it retracts that binding and its `RuleBound` results only.
- Shared authored meaning remains while another package-owned binding is active.
- Removing the final owned binding closes the package, retracts all canonical
  and typed Facts owned by it, and restores displaced authored values.
- Independently authored Facts and contributions owned by other packages remain.
- Unity's local fallback follows the same last-binding cleanup rule; gameplay
  behavior is still decided by Authority in normal connected play.
- Existing historical Facts that were never claimed by an older package are not
  guessed or deleted by name. New and migrated baselines claim typed Facts
  explicitly.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Partial removal | Selected binding disappears; sibling binding and shared meaning remain | `RemovingPreconfiguredRulesCompletesOwnedBaselineOnlyAtLastRule`; Authority meaning-package smoke |
| Final removal | Final binding closes the package and retracts its owned Triples/profiles | PlayMode final-removal assertions; Authority meaning-package smoke |
| Typed ownership | Number and boolean Facts retain their object kind in the package payload | `MeaningPackagePreservesTypedAuthoredFacts` |
| Preserve | Independent data and other package contributions survive | PlayMode independent-data assertions |

