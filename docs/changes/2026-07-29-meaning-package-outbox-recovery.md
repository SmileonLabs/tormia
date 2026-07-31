# Meaning-package outbox recovery

## Summary

- **Date:** 2026-07-29
- **Owner:** TOV development
- **Status:** Implemented and verified
- **Related issue:** World entry stopped in `Recovering` after a meaning-package edit.

## Intent

Preserve revisioned, idempotent meaning-package commands across a temporary
transport failure without allowing a historical JSON representation bug to
block all later world entry.

## Data classification

- Durable world data: unchanged; Authority remains the owner.
- Transport/infrastructure: corrected nullable command JSON and replay.
- Unity presentation: no gameplay or visual fallback was added.

## Decision and boundaries

Canonical meaning-package Facts serialize an absent `objectEntityId` as JSON
`null`. Unity also normalizes legacy pending commands immediately before replay
while preserving the command ID. Authority converts malformed payload JSON into
the normal `invalid_meaning_package_payload` rejection envelope instead of an
unhandled HTTP 500.

The outbox still does not assume a command succeeded. It removes a command only
after Authority returns a completed acceptance or rejection; an actual lost
transport response remains queued.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| New canonical meaning package | Optional entity GUID is JSON `null` | `MeaningPackageUsesJsonNullForOptionalEntityGuid` |
| Legacy queued meaning package | Empty optional GUID is normalized without changing command identity | `LegacyMeaningPackageOutboxNormalizesEmptyOptionalGuid` |
| Meaning-package removal | Empty application identity is encoded as the valid zero GUID | `MeaningPackageRemovalUsesAValidEmptyApplicationGuid` |
| Live entry with two queued packages | Both commands complete, outbox reaches zero, session reaches `InWorld` | Unity live reproduction, revision 334 |
| Malformed server payload | Authority returns a completed payload rejection rather than throwing | World Authority deserialization guard and server tests |

## Performance and multiplayer impact

Normalization is a bounded string correction performed once per recovered
meaning-package command. It creates no per-frame work, new database writes, or
new multiplayer ownership path.

## Updated documents

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
