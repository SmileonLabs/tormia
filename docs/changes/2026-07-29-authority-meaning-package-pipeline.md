# Authority-owned meaning package pipeline

## Summary

- **Date:** 2026-07-29
- **Status:** Implemented and verified
- **Request:** Let a creator change any object's complete ontology meaning
  instead of limiting behavior to its initial template defaults.

## Intent

Initial catalog triples are an editable baseline, not an immutable object type.
A tube may sink and a rock may float when the creator applies a complete
semantic package.

## Data classification

- Durable world data: authored Triples, Rule Block bindings, meaning-package
  contribution ledger, displaced authored baseline.
- Inferred state: evaluated `physical_state` such as `Floating`.
- Unity presentation: Rigidbody/buoyancy/attachment adapters synchronized from
  the accepted projection.

## Decision and boundary

`apply_meaning_package` is a revisioned, idempotent World Authority command.
It validates every Triple and published Rule Block before mutation and commits
the package in one PostgreSQL transaction. One active package owns one explicit
semantic slot per entity. Replacement restores the previous contribution's
baseline before applying the new package in the same revision.

The command contains no template, prefab, mesh, or object-name compatibility
gate. Removing a package-owned Rule Block removes the package contribution too.
Unity does not optimistically mutate coupled semantic data.

The persistent bridge can be enabled before the additive World scene. Its
dependency resolution now repairs event subscriptions idempotently whenever a
late authoring controller is discovered. This prevents a UI-only Rule Block or
meaning-package edit from being overwritten by the next Authority projection.

## Verification

| Case | Expected | Evidence |
| --- | --- | --- |
| Enabled | An arbitrary visual object changes from `HeavySinking` to `LightBuoyant`, gains `FloatableObject`, and receives its published Rule Block | `scripts/run-meaning-package-authority-smoke.ps1` |
| Removed | Removing the package retracts contributed Triple/Rule rows and restores `HeavySinking` | same smoke |
| Rule removed | Removing the package-owned Rule Block removes the complete package and restores the baseline | same smoke |
| Rejected | An invalid Triple/Rule package is rejected before mutation | `MeaningPackageContractTests` |
| Unity projection | Command-first request does not change local ontology before Authority projection | `AuthorityMeaningPackageRequestIsCompleteAndDoesNotMutateLocally` |
| Late World scene | A bridge enabled before the World editor subscribes exactly once when the editor appears | `AuthorityBridgeSubscribesWhenWorldEditorAppearsAfterBridgeEnable` |

## Performance and multiplayer impact

One creator action creates one world revision and one event. It does not add
per-frame writes. Replacement and restore execute under the existing world
transaction lock. Unity only re-synchronizes adapters for changed projected
entities.
