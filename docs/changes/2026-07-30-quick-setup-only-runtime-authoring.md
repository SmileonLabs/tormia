# Quick Setup is the only player-facing behavior authoring path

## Summary

- **Date:** 2026-07-30
- **Status:** Implemented and verified
- **Scope:** Runtime ontology editor, meaning-package authoring, harness policy

## Intent

Players should not be able to attach a Rule Block without also understanding
or supplying the Triple and optional Physical Meaning contract required by that
behavior.

## Decision and boundaries

- The Rule Block page exposes one Quick Setup picker backed by
  `RuleBlockPresetDatabase`.
- A preset submits its declared concepts, authored Facts, Rule Blocks, and
  optional Physical Meaning as one Authority-owned meaning package.
- Assigned Rule Blocks remain visible and removable, but the runtime UI no
  longer exposes bind-only add or edit controls.
- Internal controller methods may still compose bindings while building a
  complete preset/package. They are not a player-facing fallback.
- New Quick Setup choices are data additions. Unity UI code must not branch on
  a preset, prefab, mesh, or entity name.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled | One hierarchy-owned Quick Setup control applies a data-defined complete meaning package. | `RuleBlockPage_UsesQuickSetupAsItsOnlyAuthoringEntry`; Authority meaning-package tests |
| Removed/disabled | No runtime button can add or edit only a Rule Block; existing assignments can be removed. | `RuleBlockPage_UsesQuickSetupAsItsOnlyAuthoringEntry`; Rule Block removal inference test |
| Failure | Missing preset prerequisites block the package without optimistic local semantic changes. | `AuthorityMeaningPackageRequestIsCompleteAndDoesNotMutateLocally` |

## Performance and multiplayer impact

The change adds no polling or per-frame work. Durable edits continue through
one revisioned, idempotent World Authority meaning-package command.

