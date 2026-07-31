# Preconfigured Rule Blocks own their semantic baseline

## Summary

- **Date:** 2026-07-30
- **Status:** Implemented and verified
- **Scope:** World Authority meaning packages, catalog placement migration,
  runtime Rule Block removal

## Intent

A Rule Block that arrived with a placeable definition must not behave
differently from one applied through Quick Setup. Removing either kind of
assigned behavior must remove the complete semantic contribution that enabled
it, without leaving hidden Triple, Physical Meaning, or Unity fallback state.

## Decision and boundaries

- Every catalog-preconfigured behavior is represented by a
  `template_semantic_baseline` meaning package derived from
  `OntologyPlaceableDefinition`.
- The package contains the definition's initial concepts/Facts, Physical and
  Attachment profile declarations, and the default Rule Blocks that are still
  active.
- Authority can adopt exact pre-existing Facts and bindings only when no active
  package already owns them. Adoption is generic and contains no template,
  prefab, mesh, or object-name exception.
- If an unclaimed legacy binding matches the requested rule and parameters but
  uses an older immutable definition version, Authority retracts that binding
  and its inferred results and inserts the requested version under a fresh
  binding ID in the same revision.
- Existing placed entities migrate once through a semantic-contract version
  increase. Missing defaults are not adopted or recreated.
- Superseded on 2026-07-31: package-owned Rule Blocks are now removed
  independently, and the final owned binding closes the package. See
  `2026-07-31-rule-binding-granular-removal.md`.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled | A new or migrated preconfigured entity has one Authority-owned baseline package covering its active default contract. | `BaselinePackageCanRequestUnclaimedContributionAdoption`; meaning-package Authority smoke |
| Version migration | An unclaimed older Rule Block version is retired and replaced by exactly one active requested version with a fresh immutable identity. | meaning-package Authority version-migration smoke |
| Removed | Removing one owned binding preserves siblings; removing the final binding retracts baseline-owned Triples and profiles. | `RemovingPreconfiguredRulesCompletesOwnedBaselineOnlyAtLastRule`; meaning-package Authority smoke |
| Preserve | An independently authored concept/Fact or another active package's row is not claimed or removed. | PlayMode independent-data assertions; Authority adoption query |

## Performance and multiplayer impact

Migration is a one-time revisioned Authority command per eligible entity.
Removal is one atomic Authority transaction. No polling, per-frame writes, or
Unity-owned gameplay evaluation is added.
