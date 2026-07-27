# World content activation and authoritative action foundation

- **Date:** 2026-07-22
- **Status:** Implemented and harness-verified.

## Decision and boundaries

Every world now selects an enabled content package/version. Only a package
member may change that selection, and an enabled version must contain at least
one published definition.

Published `action_effect` definitions are immutable. The initial authority
execution contract deliberately accepts only one data-defined, entity-to-entity
Fact: a player supplies the registered avatar, target, optional tool, package,
and definition identity; the Authority resolves the enabled immutable payload
and writes its predicate with `source_type = action`. It advances the world
revision and is idempotent through the normal command ID.

The Authority rejects conditions, structured effects, arbitrary subject/object
patterns, and client-supplied predicates. Those capabilities remain future work
for the headless evaluator, so Unity cannot silently become the gameplay-rule
owner.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Package | A member can enable a package/version containing published content | `set_content_package` + migration `007_world_content_packages.sql` |
| Action | A registered avatar can execute only an enabled published single-triple action | `execute_action` in `WorldAuthorityRepository` |
| Disabled | A non-enabled/mismatched definition or arbitrary client predicate is rejected | same authority validation |
| Build | Authority and harness manifest build successfully | `scripts/verify-development.ps1` |
