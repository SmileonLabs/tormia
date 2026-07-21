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

## UI completion

The account UI now localizes the selected role and appends a read-only marker
for `viewer`. The world editor derives edit-button, dropdown, and input
interactivity from the bridge's single `CanEditAuthorityWorld` contract. An
offline local scene remains editable; a selected authority world is read-only
unless its role is `owner` or `editor`.
