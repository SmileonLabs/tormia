# Authority role gating for multiplayer authoring

## Summary

- **Decision:** Treat the selected world's `owner`/`editor`/`viewer` role as part of the account-world session contract.
- **Status:** Implemented and verified.
- **Scope:** Account/session data, durable world authoring, Unity presentation.

## Change

`OntologyWorldAuthorityClient` now exposes `CurrentWorldRole` and
`CanEditCurrentWorld`. `OntologyWorldAuthorityAccountEntryFlow` exposes the
selected role for UI/session consumers. The bridge only enables automatic
durable publishing for an authenticated `owner` or `editor` session. The
server remains authoritative: the command endpoint checks access, revision,
and idempotent command ID before applying a command.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| owner/editor session | Durable authoring may be sent with the current revision | Client role gate + server `CanEdit` check |
| viewer session | No automatic authoring publish; server rejects any command as `forbidden` | Client `CanEditCurrentWorld == false` + endpoint branch |
| stale revision | Command is rejected and current revision is returned | Server `stale_revision` branch |
| replayed command ID | Existing result is returned without a second world event | `FindCommandResult` replay path |

## Follow-up

The next UI slice should bind edit-button interactability and a localized
read-only message to `CanEditCurrentWorld`; it must not duplicate permission
logic in individual panels.
