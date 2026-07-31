# Harness-enforced weapon and monster production

## Summary

- Date: 2026-07-29
- Area: development policy, ontology production contracts, executable harness
- Status: implemented and verified

## Intent

Prevent a visual asset, prefab behavior, input handler, or animation-only patch
from being treated as a completed weapon or monster. Every new, migrated,
generated, or user-authored weapon and autonomous monster must follow its
canonical ontology production contract.

## Ownership decision

Authored Triples own capability and tuning. Assigned Rule Blocks own reusable
behavior. Immutable actions invoke those blocks. World Authority owns shared
evaluation and durable results. Physical Meaning, attachment profiles,
animation manifests, and Unity adapters express accepted results without
creating gameplay permission or outcomes.

## Weapon production contract

`weapon-ontology-production` requires project-owned resource registration,
weapon Triples, equipment/swing/attack Rule Blocks, matching immutable actions,
Physical Meaning and attachment/grip data, validated animation/VFX content,
Authority evaluation, Unity presentation, and executable removed-block
evidence.

## Monster production contract

`monster-ontology-production` requires project-owned visual/catalog
registration, autonomous actor Triples, an assigned autonomous Rule Block, a
matching immutable attack action, `AuthorityKinematic` Physical Meaning,
validated animation/Profile content, Authority simulation and durable combat
results, Unity interpolation/presentation, and executable removed-contract
evidence.

## Harness enforcement

`scripts/verify-development.ps1` fails when either production contract is
missing, duplicated, renamed, attached to the wrong scenario, lacks any of the
nine required stages, lacks enabled/removed expectations, has insufficient
forbidden-implementation declarations, or references evidence that is absent
from its recorded test file.

Forbidden implementations include name-based gameplay branches, Unity-owned
durable outcomes, direct action effects that duplicate Rule Block results,
fallback behavior that survives contract removal, and animation/VFX
registration that bypasses validated manifests.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Complete weapon contract | All nine stages, forbidden classes, and executable evidence are present | `rule-block-owned-primary-melee-attack` |
| Complete monster contract | All nine stages, forbidden classes, and executable evidence are present | `portable-autonomous-monster-ontology-contract` |
| Missing or bypassed stage | Development verification fails before the content is accepted as reusable production content | `scripts/verify-development.ps1` |

Final verification passed with `-RequireServices -RequireUnityMcp`: bilingual
context, core regression and production contracts, persistent Unity MCP,
World Authority Docker build, PostgreSQL/Redis/Authority services, and
Authority health.

## Updated documents

- `AGENTS.md`
- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
- `scripts/verify-development.ps1`
