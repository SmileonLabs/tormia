# Rule-package removal convergence recovery

## Summary

- **Date:** 2026-07-31
- **Status:** Implemented and verified
- **Scope:** semantic-contract version parsing, baseline meaning-package
  publication, concurrent Rule Block removal, and World Authority idempotency

## Problem

Unity read the numeric `semantic_contract_version` Fact only from its canonical
string ID. A monster already on the latest contract therefore appeared to be at
version zero, and world loading repeatedly reapplied its
`template_semantic_baseline` package. Default Triples and Rule Blocks could be
restored after a player removed them while hunting.

Deleting several Rule Block rows quickly exposed a second race. The first
request atomically retracted the whole owned package, while sibling removals
already in flight returned `not found` or a stale revision and could cause the
Unity optimistic deletion to be reverted.

## Decision and boundaries

- Semantic-contract versions are read first from the typed projection
  `objectValueJson`; legacy string Facts fall back to `objectCanonicalId`.
- A template baseline application ID is deterministically derived from entity,
  slot, and immutable package ID. Retrying the same package cannot create a new
  semantic contribution.
- Authority treats the same application/package as an unchanged success and a
  repeated removal of a historically owned binding as an idempotent success.
- Unity meaning-package and Rule Block commands retry after revision conflicts
  and do not restore a local row when Authority already matches the requested
  state.
- Removing one package-owned Rule Block atomically retracts the package-owned
  Triples, profile declarations, and sibling Rule Blocks. Independently authored
  data remains intact.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Numeric version | Typed contract version 7 resolves as 7 instead of falling back to an older legacy value | `PlacedSemanticContractReadsTypedNumericProjectionFact` |
| Repeated apply | Retrying the same application/package does not replace or duplicate active contributions | meaning-package Authority smoke |
| Concurrent remove | A late second binding removal converges after the first removal retracted the package | meaning-package Authority smoke |
| Ownership boundary | One binding retracts independently; the final binding retracts package-owned data while independent data remains | `RemovingPreconfiguredRulesCompletesOwnedBaselineOnlyAtLastRule` |

## Performance and multiplayer impact

The change operates only at command and projection boundaries. It adds no
per-frame persistence or Unity-owned gameplay evaluation. Deterministic identity
and idempotent removal make retries and concurrent client projection updates
converge on the same Authority state.
