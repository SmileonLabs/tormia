# Rule Presentation Wire Compatibility

## Summary

- **Date:** 2026-07-29
- **Owner:** TOV development harness
- **Status:** Implemented and verified
- **Related issue:** World entry stopped with
  `rule_definition_version_conflict:BuoyantWhenInWater:2`

## Intent

Add optional Rule Block runtime presentation without changing the immutable
identity of every legacy Rule Block that does not use it.

## Data classification

- [x] Durable authored world data
- [ ] Runtime observation
- [ ] Inferred state
- [x] Unity presentation
- [x] Transport / authority / infrastructure

## Decision and boundary

Unity serialization may emit an empty `runtimePresentation` object for rules
that never authored presentation. World Authority canonicalization removes only
that empty schema placeholder before checksum comparison. This makes it
wire-equivalent to the legacy payload in which the field did not exist.

A non-empty runtime presentation remains immutable rule content. It is stored,
hashed, and versioned normally. No existing database row is deleted or
rewritten.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Legacy / empty | Missing and Unity-empty presentation serialize to the same canonical payload | `EmptyRuntimePresentationPreservesLegacyImmutablePayload` |
| Configured | `AttackLight` remains in the canonical immutable payload | `ConfiguredRuntimePresentationRemainsImmutableRuleContent` |
| Live existing world | `BuoyantWhenInWater v2` keeps its checksum, the new swing rule publishes, package `3.0.0` activates, and entry succeeds | live package compatibility probe on 2026-07-29 |

Verification completed on 2026-07-29:

- World Authority tests: 40 passed
- Existing account/world package publication: passed
- World package `3.0.0`: enabled
- World entry: succeeded

## Documentation updated

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] Test scenario manifest
