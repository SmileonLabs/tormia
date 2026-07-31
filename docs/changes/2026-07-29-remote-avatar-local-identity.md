# Remote-avatar local identity exclusion

## Summary

- **Date:** 2026-07-29
- **Owner:** Codex
- **Status:** Implemented and verified
- **Request:** Prevent a white duplicate avatar from appearing after drowning
  recovery refreshes Authority motion.

## Intent

The local player must never be rendered again as a remote avatar merely because
a generic world entity was discovered first.

## Data classification

- Unity presentation: local/remote avatar exclusion and replica lifetime.
- Runtime observation: Authority motion snapshots are read unchanged.
- No account-profile or durable world-data mutation.

## Decision and boundary

The remote presenter resolves its local exclusion ID only from the registered
account-entry avatar or the actor that owns local player input. Generic
`OntologyAuthorityEntityIdentity` discovery is invalid because equipment and
placed objects use the same component.

When the stable local identity appears after the presenter, a replica keyed by
that ID is removed immediately. Authority motion and durable entities remain
unchanged.

## Ontology representation

No new Triple or Rule Block is introduced. This is presentation ownership:
the registered/local-input Actor is the local presentation, and every other
Authority avatar in the active Zone may be represented remotely.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled | Local-input actor identity replaces an arbitrary cached world identity | `LocalAvatarResolutionIgnoresArbitraryWorldIdentity` |
| Removed path | A replica matching the resolved local avatar ID is removed | Runtime MCP inspection: no `AuthorityRemoteAvatar_Player Avatar` remains |
| Regression | All PlayMode behavior remains valid | Unity PlayMode `50/50` |

## Performance and multiplayer impact

Resolution runs inside the existing presenter dependency refresh. It adds no
database write or network request. Replica removal is local presentation only.

## Updated documents

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
