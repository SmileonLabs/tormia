# Development Harness Introduction

## Summary

- **Date:** 2026-07-20
- **Owner:** Tormia development workflow
- **Status:** Implemented and verified
- **Related request:** Introduce a lightweight harness for reliable ongoing development.

## Intent

Make human/AI-assisted changes repeatable without adding a second gameplay
system. The harness should expose regressions instead of hiding them with
fallback behavior.

## Data classification

- [x] Transport / authority / infrastructure
- [x] Documentation / development process

No account profile, world fact, runtime observation, inference, or Unity
presentation data is changed by this harness.

## Decision and boundary

`AGENTS.md` defines project rules. The JSON scenario manifest names the
canonical evidence for core behavior. PowerShell scripts validate environment
contracts and launch Unity test suites. The harness never changes a world,
rule, profile, or scene while it is checking them.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Fast verification | Context pair and scenario evidence validate | Passed with `verify-development.ps1 -SkipServerBuild` |
| Authority build | Docker image builds from current source | Passed: `docker compose ... build world-authority` |
| Local services | Authority health endpoint responds | Passed: `GET /health` -> `healthy` |
| Unity PlayMode core suite | Core scenarios pass in isolation and together | Passed: 5 / 5 |
| Unity EditMode localization | Registered terms have language entries | Passed: 1 / 1 |

## Resolved regression baseline

The first harness run exposed existing failures. They were corrected at their
responsible layers rather than hidden by fallback behavior.

1. Water and proximity observation sensors now republish immediately after a
   runtime world reset/restore. They no longer wait for the normal observation
   throttle before restoring their observed Facts.
2. Water and buoyancy tests use isolated world coordinates so the loaded main
   scene's terrain and colliders cannot affect their physics assertions.
3. The physical-profile test now supplies the profile's required buoyancy rule
   ID, matching the current data-driven profile contract.
4. English and Korean fact templates for `skill_grant_requires_rule` are now
   registered in the language packs.

## Documentation updated

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [x] Core regression scenario manifest
- [x] Harness rules and scripts
